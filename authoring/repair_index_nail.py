"""Focused Blender authoring: index joint registration and v3 nail plate."""
import bpy,runpy,json,hashlib
import numpy as np
from pathlib import Path
from mathutils import Vector,Matrix,Quaternion
from mathutils.bvhtree import BVHTree
R=Path(__file__).resolve().parents[1];A=runpy.run_path(str(R/'authoring/hand_joint_registration.py'));O=A['O'];M=A['M'];T=A['T']
def rounded_nail(v,b,ss,nail):
 p=v['p'].astype(float);j=np.linalg.inv(b)[:,:3,3];axis=j[27]-j[26];axis/=np.linalg.norm(axis);ctr=p[nail].mean(0)-axis*.003
 tw=np.where(v['ids']>=24,v['w'],0).sum(1);tri=np.concatenate(ss);tr=tri[np.all(tw[tri]>.5,axis=1)];sec=runpy.run_path(str(R/'authoring/repair_thumb_distal.py'))['section_center']
 cen=sec(p,tr,j[26],axis,(ctr-j[26])@axis);normal=ctr-cen;normal-=axis*(normal@axis);normal/=np.linalg.norm(normal);side=np.cross(normal,axis);side/=np.linalg.norm(side)
 tree=BVHTree.FromPolygons(p.tolist(),tr.tolist(),all_triangles=True);points=[];normals=[];misses=0
 segments=32;rings=8;lift=.00035
 for radius,theta in [(0,0)]+[(r,t) for r in np.linspace(1/rings,1,rings) for t in np.linspace(0,2*np.pi,segments,endpoint=False)]:
  along=(ctr-j[26])@axis+.005*np.cos(theta)*radius
  a=sec(p,tr,j[26],axis,along)+side*(.002*np.sin(theta)*radius)
  loc,n,_,_=tree.ray_cast(Vector(a),Vector(normal),.035)
  if loc is None:
   misses+=1;loc,n,_,_=tree.find_nearest(Vector(a+normal*.003))
  nn=np.array(n);nn*=np.sign(nn@normal);points.append(np.array(loc)+nn*lift);normals.append(nn)
 assert misses==0,('Nail footprint outside dorsal pad',misses)
 c=np.zeros(len(v)+len(points),dtype=v.dtype);c[:len(v)]=v;c['p'][len(v):]=points;c['n'][len(v):]=normals;c['ids'][len(v):]=[26,0,0,0];c['w'][len(v):]=[1,0,0,0]
 # Repaint the old jagged tip cap with an adjacent skin texel. The new plate
 # uses the original teal texture, with its own rounded geometric boundary.
 skin=[i for i in np.flatnonzero(tw>.8) if i not in nail];skin=min(skin,key=lambda i:np.linalg.norm(p[i]-(ctr-axis*.015)))
 c['uv'][nail]=v['uv'][skin];c['uv'][len(v):]=v['uv'][nail[len(nail)//2]]
 faces=[];offset=len(v)
 for i in range(segments):faces.append([offset,offset+1+i,offset+1+(i+1)%segments])
 for ring in range(rings-1):
  a=offset+1+ring*segments;z=a+segments
  for i in range(segments):
   k=(i+1)%segments;faces.extend([[a+i,z+i,z+k],[a+i,z+k,a+k]])
 for f in faces:
  a,z,t=c['p'][f]
  if np.cross(z-a,t-a)@normal>0:f[1],f[2]=f[2],f[1]
 sections=[x.copy() for x in ss];sections[0]=np.concatenate([sections[0],np.array(faces,dtype=np.int32)])
 return c,sections,{'vertices':list(range(len(v),len(c))),'repaintedTipVertices':nail,'length':.010,'width':.004,'surfaceOffset':lift,'rayMisses':misses}

def build(render=True):
 report={}
 for var in ('v1','v3'):
  raw=(O/'original'/f'{var}.mikumesh').read_bytes();parts=M['chunks'](raw);_,v,b,ss=M['read'](parts['arm-left']);starts=(4,) if var=='v1' else (9,);c=A['center_fingers'](A['fingers'](v,b,starts),b,ss,starts,False)
  if var=='v3':
   # The Plate handle sits between the native finger's bone axis and pad.
   # Retain Glider's measured pad offset for this thicker index finger.
   base=A['fingers'](c,b,(4,));centered=A['center_fingers'](base.copy(),b,ss,(4,),False);native=A['center_fingers'](base.copy(),b,ss,(4,),True)
   joints=np.linalg.inv(b)[:,:3,3];axis=joints[8]-joints[5];axis/=np.linalg.norm(axis)
   # Keep the native proximal pad offset, blending to the human distal
   # phalanx center before the nail. A full distal offset clips the moving
   # Plate model at recorded inspect frame 178.
   proximal=1-A['smooth'](((base['p']-joints[6])@axis)/((joints[7]-joints[6])@axis))
   c=centered;c['p']+=(native['p']-centered['p'])*proximal[:,None]
  row={'inputSha256':hashlib.sha256(raw).hexdigest()}
  A['normals'](c,v['p'],np.concatenate(ss),np.linalg.norm(c['p']-v['p'],axis=1)>1e-8)
  row['editedVertices']=int((np.linalg.norm(c['p']-v['p'],axis=1)>1e-8).sum());row['weightsChanged']=int(np.any(c['w']!=v['w'],axis=1).sum())
  if var=='v3':c,ss,row['nail']=rounded_nail(c,b,ss,json.loads((O/'diagnosis.json').read_text())[var]['nail'])
  image=bpy.data.images.load(str(R/('assets/V1/textures/body-albedo.png' if var=='v1' else 'assets/v3/textures/body-albedo.png')),check_existing=True);pixels=np.empty(image.size[0]*image.size[1]*4,dtype=np.float32);image.pixels.foreach_get(pixels);pixels=pixels.reshape(image.size[1],image.size[0],4);uv=c['uv']%1;rgb=pixels[(uv[:,1]*(image.size[1]-1)).astype(int),(uv[:,0]*(image.size[0]-1)).astype(int),:3];indexWeight=np.where((c['ids']>=4)&(c['ids']<=8),c['w'],0).sum(1)
  row['indexNailVertices']=np.flatnonzero((indexWeight>.5)&(rgb[:,1]>rgb[:,0]*1.4)&(rgb[:,2]>rgb[:,0]*1.3)).tolist()
  parts['arm-left']=M['write']('arm-left',c,b,ss);result=M['archive'](parts);(O/f'{var}.mikumesh').write_bytes(result);row['modelSha256']=hashlib.sha256(result).hexdigest()
  profile=json.loads((O/'original'/f'{var}-corrections.json').read_text())
  (O/f'{var}-corrections.json').write_text(json.dumps(profile,indent=2));(O/f'{var}-arm-left-candidate.json').write_text(json.dumps(dict(positions=c['p'].tolist(),normals=c['n'].tolist(),uv=c['uv'].tolist(),sections=[s.ravel().tolist() for s in ss],indices=c['ids'].tolist(),weights=c['w'].tolist())));report[var]=row
 (O/'candidate-report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report),flush=True)
 if render:A['A']['review']('candidate')
if __name__=='__main__':build()
