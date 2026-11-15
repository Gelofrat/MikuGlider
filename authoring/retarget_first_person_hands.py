"""Run through Blender MCP. Bake finger retargeting into the six FP arm meshes.

The game has A (metacarpal), B (proximal), C (middle), D (distal), End.
The imported human rigs start at the proximal phalanx, not the metacarpal.
Thumb A/B/C already has the correct semantics and must not be shifted.
No animation curves, runtime bones, vertex counts or triangle lists are added.
"""
import io
import json
import struct
import hashlib
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
PATHS = {'v1': 'assets/MikuGlider.mikumesh',
         'v2': 'assets/v2/MikuGliderV2.mikumesh',
         'v3': 'assets/v3/MikuGliderV3.mikumesh'}
DT = np.dtype([('p','<f4',3),('n','<f4',3),('uv','<f4',2),('w','<f4',4),('ids','<i4',4)])

def chunks(data):
    f = io.BytesIO(data)
    assert f.read(8) == b'MIKUMSH1'
    result = {}
    for _ in range(struct.unpack('<i', f.read(4))[0]):
        start = f.tell()
        n = struct.unpack('<i', f.read(4))[0]
        name = f.read(n).decode()
        nv, ns, nb = struct.unpack('<iii', f.read(12))
        f.seek(nb*64 + nv*64, 1)
        for _ in range(ns):
            count = struct.unpack('<i', f.read(4))[0]
            f.seek(count*4, 1)
        result[name] = data[start:f.tell()]
    assert f.tell() == len(data)
    return result

def read(chunk):
    f=io.BytesIO(chunk); n=struct.unpack('<i',f.read(4))[0]; name=f.read(n).decode()
    nv,ns,nb=struct.unpack('<iii',f.read(12))
    binds=np.frombuffer(f.read(nb*64),'<f4').reshape(-1,4,4).copy()
    vertices=np.frombuffer(f.read(nv*64),DT).copy(); sections=[]
    for _ in range(ns):
        count=struct.unpack('<i',f.read(4))[0]
        sections.append(np.frombuffer(f.read(count*4),'<i4').reshape(-1,3).copy())
    return name,vertices,binds,sections

def write(name,vertices,binds,sections):
    encoded=name.encode()
    return (struct.pack('<i',len(encoded))+encoded+
            struct.pack('<iii',len(vertices),len(sections),len(binds))+
            binds.astype('<f4').tobytes()+vertices.astype(DT).tobytes()+
            b''.join(struct.pack('<i',t.size)+t.astype('<i4').tobytes() for t in sections))

def archive(parts):
    return b'MIKUMSH1'+struct.pack('<i',len(parts))+b''.join(parts.values())

def correct(vertices,binds,sections):
    v=vertices; c=v.copy(); rest=np.linalg.inv(binds)[:,:3,3]
    # Preserve finger thickness; only refit length and the matching joint.
    for slot in range(4):
        for bone in range(4,24):
            if (bone-4)%5==4: continue
            selected=v['ids'][:,slot]==bone
            if (bone-4)%5==3:
                # v2's source end marker was incorrectly fitted to D. Place
                # it at End but skin it with D, its deforming parent.
                dest=v['p'][selected]+rest[bone+1]-rest[bone]; target=bone
            else:
                a=rest[bone+1]-rest[bone]; z=rest[bone+2]-rest[bone+1]
                axis=a/np.linalg.norm(a); target=bone+1
                dest=(v['p'][selected]+rest[target]-rest[bone]+
                      np.outer((v['p'][selected]-rest[bone])@axis,z/np.linalg.norm(a)-axis))
            c['p'][selected]+=v['w'][selected,slot,None]*(dest-v['p'][selected])
            c['ids'][selected,slot]=target
    for i in range(len(c)):
        values={}
        for bone,weight in zip(c['ids'][i],c['w'][i]):
            if weight>0: values[int(bone)]=values.get(int(bone),0)+float(weight)
        if len(values)==int((c['w'][i]>0).sum()): continue
        c['ids'][i]=0; c['w'][i]=0
        for slot,(bone,weight) in enumerate(sorted(values.items(),key=lambda x:-x[1])):
            c['ids'][i,slot]=bone; c['w'][i,slot]=weight
    # Thumb support is rebuilt from its own three segments, retaining the
    # imported thumb/palm partition. Distal weights must not pull the web.
    joints=rest[24:28]; lengths=np.linalg.norm(np.diff(joints,axis=0),axis=1)
    arc=np.r_[0,np.cumsum(lengths)]
    total=np.where((c['ids']>=24)&(c['ids']<=26),c['w'],0).sum(1)
    for i in np.flatnonzero(total>1e-6):
        projections=[]; p=c['p'][i]
        for k in range(3):
            axis=(joints[k+1]-joints[k])/lengths[k]; t=np.dot(p-joints[k],axis)
            q=joints[k]+axis*np.clip(t,0,lengths[k])
            projections.append((np.linalg.norm(p-q),arc[k]+t))
        distance=min(projections)[1]
        s=np.clip((distance-arc[1]+.008)/.016,0,1); s=s*s*(3-2*s)
        t=np.clip((distance-arc[2]+.008)/.016,0,1); t=t*t*(3-2*t)
        values={}
        for bone,weight in zip(c['ids'][i],c['w'][i]):
            if weight>1e-7 and bone<24: values[int(bone)]=values.get(int(bone),0)+float(weight)
        for bone,weight in zip([24,25,26],[1-s,s*(1-t),t]):
            if weight>1e-6: values[bone]=float(total[i])*weight
        pairs=sorted(values.items(),key=lambda x:-x[1])[:4]; norm=sum(w for _,w in pairs)
        c['ids'][i]=0; c['w'][i]=0
        for slot,(bone,weight) in enumerate(pairs): c['ids'][i,slot]=bone; c['w'][i,slot]=weight/norm
    # Recompute only changed geometry; weld UV duplicates for smooth shading.
    tri=np.concatenate(sections); normals=np.zeros((len(c),3))
    cross=np.cross(c['p'][tri[:,2]]-c['p'][tri[:,0]],c['p'][tri[:,1]]-c['p'][tri[:,0]])
    for k in range(3): np.add.at(normals,tri[:,k],cross)
    touched=np.linalg.norm(c['p']-v['p'],axis=1)>1e-7; groups={}
    for i,p in enumerate(v['p']): groups.setdefault(tuple(np.round(p,5)),[]).append(i)
    for ids in groups.values():
        if touched[ids].any():
            n=normals[ids].sum(0); length=np.linalg.norm(n)
            if length>1e-10: c['n'][ids]=n/length
    assert np.isfinite(c['p']).all() and np.isfinite(c['w']).all()
    assert np.max(abs(c['w'].sum(1)-1))<1e-5
    assert (c['w']>=0).all() and (c['ids']<len(binds)).all()
    return c

def apply():
    """Requires the reviewed 0.12.21 inputs; refuses to apply twice."""
    manifest=json.loads((ROOT/'authoring/first-person-hands-inputs.json').read_text())
    for variant,path in PATHS.items():
        data=(ROOT/path).read_bytes()
        if hashlib.sha256(data).hexdigest()!=manifest[variant]:
            raise ValueError('Input changed/already corrected: '+path)
    report={}
    for variant,path in PATHS.items():
        parts=chunks((ROOT/path).read_bytes()); report[variant]={}
        for name in ['arm-left','arm-right']:
            _,v,b,s=read(parts[name]); corrected=correct(v,b,s)
            parts[name]=write(name,corrected,b,s)
            report[variant][name]={'vertices':len(v),'triangles':sum(len(x) for x in s),
                'bones':len(b),'maxInfluences':int((corrected['w']>0).sum(1).max()),
                'verticesMoved':int((np.linalg.norm(corrected['p']-v['p'],axis=1)>1e-7).sum())}
        (ROOT/path).write_bytes(archive(parts))
        report[variant]['sha256']=hashlib.sha256((ROOT/path).read_bytes()).hexdigest()
    (ROOT/'authoring/first-person-hands-export.json').write_text(json.dumps(report,indent=2)+'\n')
    return report

if __name__=='__main__':
    print(json.dumps(apply()))
