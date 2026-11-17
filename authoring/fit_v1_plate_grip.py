"""Author a contact-constrained human grip through Blender MCP.

The reference wrist, metacarpal roots, joint spacing and native animation remain
fixed. Candidates are saved separately until Unity replay and surface checks pass.
"""
import bpy, runpy, json, hashlib, math, copy
from pathlib import Path
import numpy as np
from mathutils import Matrix, Euler, Vector
from mathutils.bvhtree import BVHTree

R=Path(__file__).resolve().parents[1];O=R/'authoring/grip-fix';O.mkdir(exist_ok=True)
M=runpy.run_path(str(R/'authoring/retarget_first_person_hands.py'))
T=runpy.run_path(str(R/'authoring/thumb_grip_tools.py'))
Q=runpy.run_path(str(R/'authoring/check_plate_release.py'))
V=runpy.run_path(str(R/'authoring/review_thumb_return.py'));V['render'].__globals__['O']=O
CAP=Path('C:/Apps/Steam/steamapps/common/Mycopunk/BepInEx/MikuGliderDiagnostics/captures/20261116-180944-759-d974ee')
STARTS={'index':4,'middle':9,'ring':14,'pinky':19,'thumb':24}

def load():
    original=O/'original';original.mkdir(exist_ok=True)
    for src,name in [(R/'assets/V1/MikuGlider.mikumesh','v1.mikumesh'),(R/'assets/V1/corrections.json','v1-corrections.json')]:
        p=original/name
        if not p.exists():p.write_bytes(src.read_bytes())
    _,v,b,ss=M['read'](M['chunks']((original/'v1.mikumesh').read_bytes())['arm-left'])
    d=json.loads((R/'scratch/grip-reference/v1-0.json').read_text());bones=np.array(d['bones']).reshape(-1,4,4)
    return v,b,ss,d,bones

def frames(b):
    j=np.linalg.inv(b)[:,:3,3];result={}
    for name,start in STARTS.items():
        for i in range(start,start+4 if start<24 else 27):
            y=j[i+1]-j[i];y/=np.linalg.norm(y);z=np.array([0.,-1.,0.]);z-=y*(z@y);z/=np.linalg.norm(z);x=np.cross(y,z)
            f=np.eye(4);f[:3,:3]=np.column_stack([x,y,z]);f[:3,3]=j[i];result[i]=f
    return result

def angles(b,bones):
    native=bones@b;f=frames(b);out={}
    for name,start in STARTS.items():
        out[name]=[]
        for i in range(start+1 if start<24 else start,start+4 if start<24 else 27):
            parent=3 if i==24 else i-1
            local=np.linalg.inv(f[i])@np.linalg.inv(native[parent])@native[i]@f[i]
            out[name].append(np.degrees(list(Matrix(local.tolist()).to_euler('XYZ'))).tolist())
    return out

def deform(b,bones,controls):
    native=bones@b;target=native.copy();f=frames(b)
    for name,values in controls.items():
        if name=='metacarpals':continue
        start=STARTS[name];ids=range(start+1 if start<24 else start,start+4 if start<24 else 27)
        if name in controls.get('metacarpals',{}):
            rotation=controls['metacarpals'][name]
            target[start]=native[start]@f[start]@np.array(Euler(np.radians(rotation),'XYZ').to_matrix().to_4x4())@np.linalg.inv(f[start])
        for i,rotation in zip(ids,values):
            parent=3 if i==24 else i-1
            r=np.array(Euler(np.radians(rotation),'XYZ').to_matrix().to_4x4())
            target[i]=target[parent]@f[i]@r@np.linalg.inv(f[i])
        target[start+4 if start<24 else 27]=target[i]
    return target

def surface(v,b,bones,base,target):
    native=bones@b;p=np.c_[v['p'],np.ones(len(v))];delta=np.zeros_like(p)
    for k in range(4):delta+=np.einsum('nij,nj->ni',(target-native)[v['ids'][:,k]],p)*v['w'][:,k,None]
    return base+delta[:,:3]

def qmul(a,b):
    return np.c_[a[:,0]*b[:,0]-np.sum(a[:,1:]*b[:,1:],axis=1),a[:,0,None]*b[:,1:]+b[:,0,None]*a[:,1:]+np.cross(a[:,1:],b[:,1:])]

def volume_surface(v,b,bones,target):
    # Dual-quaternion joint volume is authored here and baked into the pose
    # corrective. Runtime remains Unity's ordinary four-weight skinning.
    helper,low,fraction=T['B']['wrist'](v,b,bones,0);matrices=np.concatenate([target,helper]);core=(v['ids']>=1)&(v['ids']<=3)
    indices=np.c_[v['ids'],28+low,29+low];cw=np.where(core,v['w'],0).sum(1)
    weights=np.c_[np.where(core,0,v['w']),cw*(1-fraction),cw*fraction]
    q=np.array([list(Matrix(m.tolist()).to_quaternion().normalized()) for m in matrices]);t=np.c_[np.zeros(len(matrices)),matrices[:,:3,3]];dual=qmul(t,q)*.5
    chosen=q[indices[np.arange(len(v)),weights.argmax(1)]];sign=np.where(np.sum(q[indices]*chosen[:,None,:],axis=2)<0,-1.,1.)
    a=np.sum(q[indices]*weights[:,:,None]*sign[:,:,None],axis=1);z=np.sum(dual[indices]*weights[:,:,None]*sign[:,:,None],axis=1);length=np.linalg.norm(a,axis=1);a/=length[:,None];z/=length[:,None];z-=a*np.sum(a*z,axis=1)[:,None]
    conjugate=a.copy();conjugate[:,1:]*=-1;translation=2*qmul(z,conjugate)[:,1:]
    scale=np.linalg.norm(matrices[:,:3,:3],axis=1).mean(1);s=np.sum(scale[indices]*weights,axis=1)
    point=np.c_[np.zeros(len(v)),v['p']*s[:,None]];rotated=qmul(qmul(a,point),conjugate)[:,1:]
    return rotated+translation

def save_candidate(name,controls,volume=False):
    v,b,ss,d,bones=load();target=deform(b,bones,controls);base=np.array(d['positions']);p=surface(v,b,bones,base,target)
    mat,_=T['blend'](v,b,bones,'v1')
    if volume:
        raw=np.einsum('nij,nj->ni',mat,np.c_[v['p'],np.ones(len(v))])[:,:3]
        linear=surface(v,b,bones,raw,target);dq=volume_surface(v,b,bones,target)
        weight=np.where((v['ids']>=4)&(v['ids']<24),v['w'],0).sum(1);u=np.clip((weight-.4)/.5,0,1);u=u*u*(3-2*u)
        p+=(dq-linear)*u[:,None]
    delta=np.linalg.solve(mat,np.c_[p,np.ones(len(p))][:,:,None])[:,:3,0]-v['p']
    np.savez(O/(name+'.npz'),points=p,delta=delta,transforms=target)
    (O/(name+'.json')).write_text(json.dumps({'controls':controls,'volume':volume,'modelSha256':hashlib.sha256((O/'original/v1.mikumesh').read_bytes()).hexdigest(),'maximumRestDelta':float(np.linalg.norm(delta,axis=1).max())},indent=2))
    print(name,'saved; max rest delta',float(np.linalg.norm(delta,axis=1).max()),flush=True)
    return p

def render(name,views=('game','palm')):
    v,b,ss,d,bones=load();p=np.load(O/(name+'.npz'))['points'];cam=np.array(d['camera']['firstPersonPass']['worldToCamera']).reshape(4,4);cam[:3,3]+=cam[:3,:3]@d['origin']
    p=(np.c_[p,np.ones(len(p))]@cam.T)[:,:3];j=(cam@bones)[:,:3,3];center=j[[5,6,10,11,15,16,20,21]].mean(0)
    normal=np.cross(j[5]-j[20],j[10]-j[3]);normal/=np.linalg.norm(normal);side=j[5]-j[20];side/=np.linalg.norm(side)
    eyes={'game':np.zeros(3),'palm':center-normal*.15,'side':center+side*.15,'dorsal':center+normal*.15}
    weapons=[dict(w,positions=(np.c_[w['positions'],np.ones(len(w['positions']))]@cam.T)[:,:3]) for w in d['weapons']]
    for view in views:V['render'](name+'-'+view,p,np.concatenate(ss),v['uv'],R/'assets/V1/textures/body-albedo.png',center,eyes[view],weapons,scale=.075)

def initial():
    v,b,ss,d,bones=load();original=angles(b,bones);target=deform(b,bones,original);recreated=surface(v,b,bones,np.array(d['positions']),target)
    error=float(np.linalg.norm(recreated-np.array(d['positions']),axis=1).max());assert error<2e-5,error
    print('Kinematic round trip error',error,'native angles',original,flush=True)
    (O/'native-controls.json').write_text(json.dumps(original,indent=2))
    controls=copy.deepcopy(original)
    for name,flexion in {'index':(90,90,60),'middle':(88,92,62),'ring':(88,95,60),'pinky':(85,90,55)}.items():
        for i,angle in enumerate(flexion):controls[name][i][0]=angle
        controls[name][0][1]=0
    save_candidate('bounded-grip',controls)
    render('bounded-grip')

def measure(name):
    v,b,ss,d,bones=load();base=np.array(d['positions']);p=np.load(O/(name+'.npz'))['points'];tri=np.concatenate(ss);j=np.linalg.inv(b)[:,:3,3]
    fw=np.where(v['ids']>=4,v['w'],0).sum(1);tr=tri[np.any(fw[tri]>.2,axis=1)]
    shape=runpy.run_path(str(R/'authoring/hand_quality.py'))['surface_metrics'](base,p,tr)
    weapons=d['weapons'];w=next(w for w in weapons if w['name']=='Native weapon Handle');tree=BVHTree.FromPolygons(w['positions'],np.array(w['triangles']).reshape(-1,3).tolist(),all_triangles=True)
    report={'shape':shape,'stages':{}}
    for label,points in [('before',base),('candidate',p)]:
        out={}
        for finger,start in STARTS.items():
            end=start+5 if start<24 else 28;total=np.where((v['ids']>=start)&(v['ids']<end),v['w'],0).sum(1);selected=tri[np.all(total[tri]>.8,axis=1)]
            row={'weaponCrossingPairs':Q['C']['contact'](points,selected,weapons)['interiorTrianglePairs'],'phalanges':[]}
            if finger!='thumb':
                for i in range(start+1,start+4):
                    direction=j[i+1]-j[i];length=np.linalg.norm(direction);direction/=length;t=(v['p']-j[i])@direction
                    ids=np.flatnonzero((total>.9)&(t>length*.25)&(t<length*.95));pad=ids[v['p'][ids,1]<=np.quantile(v['p'][ids,1],.35)]
                    distances=[tree.find_nearest(Vector(points[k]))[3] for k in pad]
                    row['phalanges'].append({'bone':i,'minPadGap':min(distances),'medianPadGap':float(np.median(distances))})
            out[finger]=row
        report['stages'][label]=out
    (O/(name+'-metrics.json')).write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2),flush=True)
    return report

def fit_contacts(seed='bounded-grip',name='contact-grip'):
    import bmesh
    v,b,ss,d,bones=load();native=bones@b;f=frames(b);j=np.linalg.inv(b)[:,:3,3];base=np.array(d['positions']);tri=np.concatenate(ss)
    controls=json.loads((O/(seed+'.json')).read_text())['controls'];original=angles(b,bones)
    w=next(w for w in d['weapons'] if w['name']=='Native weapon Handle')
    mesh=bpy.data.meshes.new('Closed contact proxy');mesh.from_pydata(w['positions'],[],np.array(w['triangles']).reshape(-1,3).tolist());bm=bmesh.new();bm.from_mesh(mesh)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-6);boundary=sum(e.is_boundary for e in bm.edges)
    caps=bmesh.ops.holes_fill(bm,edges=[e for e in bm.edges if e.is_boundary],sides=0)['faces'];bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    assert all(e.is_manifold for e in bm.edges),'Handle proxy is not closed'
    tree=BVHTree.FromBMesh(bm);bm.free();bpy.data.meshes.remove(mesh)
    total=np.where(v['ids']>=4,v['w'],0).sum(1)
    # Palmar skin only: a small-diameter grip can end against the palm.
    mask=(total<.4)&(v['p'][:,0]<j[3,0]+.025)&(v['p'][:,1]<j[3,1])
    palm_tri=tri[np.all(mask[tri],axis=1)]
    palm=BVHTree.FromPolygons(base.tolist(),palm_tri.tolist(),all_triangles=True)
    report={'proxyBoundaryEdgesCapped':boundary,'proxyCapsAdded':len(caps),'fingers':{},'notes':'Fixed wrist and fixed bone lengths. Closed proxy caps only the missing mount-end face; final intersections use original weapon triangles.'}
    for finger,start in list(STARTS.items())[:4]:
        target=deform(b,bones,controls);current=surface(v,b,bones,base,target)
        fw=np.where((v['ids']>=start)&(v['ids']<=start+4),v['w'],0).sum(1);ids=np.flatnonzero(fw>.65);p0=current[ids];hom=np.c_[v['p'][ids],np.ones(len(ids))]
        selected=[]
        for i in range(start+1,start+4):
            u=j[i+1]-j[i];length=np.linalg.norm(u);u/=length;t=(v['p'][ids]-j[i])@u
            region=np.flatnonzero((t>length*.3)&(t<length*.95)&(fw[ids]>.9));region=region[v['p'][ids[region],1]<=np.quantile(v['p'][ids[region],1],.35)];selected.append(region)
        q=np.array([controls[finger][0][0],controls[finger][0][1],controls[finger][0][2],controls[finger][1][0],controls[finger][2][0]],float)
        spread=original[finger][0][2];bounds=np.array([[50,90],[-10,10],[max(-20,spread-10),min(20,spread+10)],[35,110],[15,85]],float)
        weights=np.array([np.where(v['ids'][ids]==i,v['w'][ids],0).sum(1) for i in range(start+1,start+5)])
        calls=0
        def evaluate(q,details=False):
            nonlocal calls
            calls+=1;rotations=[[q[0],q[1],q[2]],[q[3],0,0],[q[4],0,0]];out={start:target[start]}
            for i,r in zip(range(start+1,start+4),rotations):out[i]=out[i-1]@f[i]@np.array(Euler(np.radians(r),'XYZ').to_matrix().to_4x4())@np.linalg.inv(f[i])
            out[start+4]=out[start+3];points=p0.copy()
            for k,i in enumerate(range(start+1,start+5)):points+=(hom@(out[i]-target[i]).T)[:,:3]*weights[k,:,None]
            gaps=[];distances=[]
            for pt in points:
                loc,n,_,distance=tree.find_nearest(Vector(pt));gaps.append((Vector(pt)-loc).dot(n)*1000);distances.append(distance*1000)
            gaps=np.array(gaps);distances=np.array(distances);inside=np.maximum(0,.10-gaps)
            penetration=40*np.mean(inside**2)+100*np.max(inside)**2
            contact=0.
            for segment,region in enumerate(selected):
                dist=distances[region].copy()
                if segment==2:
                    hand=np.array([palm.find_nearest(Vector(points[i]))[3]*1000 for i in region]);dist=np.minimum(dist,hand)
                contact+=float(np.mean(np.sort(dist)[:max(3,len(region)//3)]**2))
            comfort=.0005*((q[0]-85)**2+.3*(q[3]-90)**2+1.5*(q[4]-.65*q[3])**2+2*q[1]**2+2*(q[2]-spread)**2)
            score=penetration+contact+comfort
            return {'cost':score,'penetrationPenalty':penetration,'contactPenalty':contact,'maxNegativeProxyGap':max(0.,-float(gaps.min()))/1000,'controls':rotations} if details else score
        q=np.clip(q,bounds[:,0],bounds[:,1]);initial=evaluate(q,True);best=evaluate(q)
        for step in (12,6,3,1.5,.75):
            for iteration in range(10):
                improved=False
                directions=[np.eye(5)[i] for i in range(5)]+[np.array([0,0,0,1,.65]),np.array([1,0,0,-1,0])]
                for axis in directions:
                    for sign in (-1,1):
                        candidate=np.clip(q+axis*step*sign,bounds[:,0],bounds[:,1]);value=evaluate(candidate)
                        if value<best-1e-7:q=candidate;best=value;improved=True
                if not improved:break
        final=evaluate(q,True);controls[finger]=final['controls'];report['fingers'][finger]={'initial':initial,'final':final,'evaluations':calls};print(finger,final,flush=True)
    (O/(name+'-search.json')).write_text(json.dumps(report,indent=2));save_candidate(name,controls);measure(name)

def profile(name='contact-grip'):
    v,b,ss,d,bones=load();info=json.loads((O/(name+'.json')).read_text());data=np.load(O/(name+'.npz'));target_bones=data['transforms']@np.linalg.inv(b)
    reference=Q['BA']['reference'][0]['bones'];edits=[]
    for finger,start in STARTS.items():
        if start==24 and 'thumbFit' not in info:continue
        for i in range(start if start==24 or 'metacarpals' in info['controls'] else start+1,27 if start==24 else start+4):
            parent=reference[i]['parentBoneIndex']
            old=Matrix((np.linalg.inv(bones[parent])@bones[i]).tolist()).to_quaternion().normalized()
            new=Matrix((np.linalg.inv(target_bones[parent])@target_bones[i]).tolist()).to_quaternion().normalized()
            q=old.inverted()@new;q.normalize()
            q=new
            if q.w<0:q.negate()
            edits.append({'bone':i,'name':reference[i]['name'],'rotation':dict(x=q.x,y=q.y,z=q.z,w=q.w),'absolute':True})
    source=json.loads((O/'original/v1-corrections.json').read_text());drivers=[]
    for i in (5,6,7):
        parent=reference[i]['parentBoneIndex'];q=Matrix((np.linalg.inv(bones[parent])@bones[i]).tolist()).to_quaternion().normalized()
        drivers.append({'bone':i,'name':reference[i]['name'],'rotation':dict(x=q.x,y=q.y,z=q.z,w=q.w),'innerRadius':5,'radius':60})
    pose={'name':'Plate human finger grip','sourcePose':'Blender contact-constrained finger rig; fixed wrist and phalanx lengths','weapon':'YAPlateLauncher','group':'fingers','drivers':drivers,'boneRotations':edits,'vertices':[],'deltas':[]}
    if (O/'handle-anchor-matrix.json').exists():
        anchor=np.array(json.loads((O/'handle-anchor-matrix.json').read_text()));q=Matrix(anchor.tolist()).to_quaternion().normalized()
        pose['gripAnchor']={'bone':3,'name':reference[3]['name'],'path':'MagRoot/Handle','position':dict(zip(('x','y','z'),map(float,anchor[:3,3]))),'rotation':dict(x=q.x,y=q.y,z=q.z,w=q.w),'innerDistance':.02,'outerDistance':.06}
    source['meshes'][0]['poses'].append(pose)
    E=runpy.run_path(str(R/'authoring/pose_corrections.py'))
    result,posed,weights=E['evaluate'](v,b,bones,source,'v1','YAPlateLauncher')
    # Only author the small residual skin correction after applying the actual
    # bone pose. Do not encode the entire finger motion in a shape key.
    working_v=E['apply_weights'](v,source);mat,_=T['blend'](working_v,b,posed,'v1');difference=data['points']-result
    delta=np.linalg.solve(mat[:,:3,:3],difference[:,:,None])[:,:,0]
    active=np.flatnonzero(np.linalg.norm(delta,axis=1)>3e-5)
    pose['vertices']=active.tolist();pose['deltas']=[dict(zip(('x','y','z'),map(float,row))) for row in delta[active]]
    (O/'v1-corrections.json').write_text(json.dumps(source,indent=2)+'\n')
    (O/'v1-arm-left-candidate.json').write_text(json.dumps({'positions':v['p'].tolist(),'normals':v['n'].tolist(),'uv':v['uv'].tolist(),'sections':[t.ravel().tolist() for t in ss],'indices':v['ids'].tolist(),'weights':v['w'].tolist()}))
    expected=E['evaluate'](v,b,bones,source,'v1','YAPlateLauncher')[0];error=float(np.linalg.norm(expected-data['points'],axis=1).max())
    print('Profile exported',len(edits),'bone edits',len(active),'residual skin vertices; reconstruction error',error,flush=True)
    assert error<2e-5,error

def relax_surface(seed='contact-grip',name='relaxed-grip'):
    """ARAP skin correction with fixed nail/shaft anchors and handle contact."""
    import bmesh
    v,b,ss,d,bones=load();data=np.load(O/(seed+'.npz'));goal=data['points'];tri=np.concatenate(ss);base=np.array(d['positions'])
    w=next(w for w in d['weapons'] if w['name']=='Native weapon Handle');mesh=bpy.data.meshes.new('Grip constraint proxy');mesh.from_pydata(w['positions'],[],np.array(w['triangles']).reshape(-1,3).tolist());bm=bmesh.new();bm.from_mesh(mesh)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-6);bmesh.ops.holes_fill(bm,edges=[e for e in bm.edges if e.is_boundary],sides=0);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));assert all(e.is_manifold for e in bm.edges)
    tree=BVHTree.FromBMesh(bm);bm.free();bpy.data.meshes.remove(mesh)
    image=bpy.data.images.load(str(R/'assets/V1/textures/body-albedo.png'),check_existing=True);pixels=np.empty(image.size[0]*image.size[1]*4,np.float32);image.pixels.foreach_get(pixels);pixels=pixels.reshape(image.size[1],image.size[0],4);uv=v['uv']%1;rgb=pixels[(uv[:,1]*(image.size[1]-1)).astype(int),(uv[:,0]*(image.size[0]-1)).astype(int),:3]
    fw=np.where((v['ids']>=4)&(v['ids']<24),v['w'],0).sum(1);nails=(fw>.5)&(rgb[:,1]>rgb[:,0]*1.4)&(rgb[:,2]>rgb[:,0]*1.3)
    _,inverse=np.unique(np.round(v['p'],6),axis=0,return_inverse=True);count=inverse.max()+1;counts=np.bincount(inverse)
    average=lambda array:np.column_stack([np.bincount(inverse,weights=array[:,i],minlength=count)/counts for i in range(3)])
    x=average(goal);anchors=x.copy();scale=float(np.linalg.norm((bones[3]@b[3])[:3,:3],axis=0).mean());rest=average(v['p'])*scale
    faces=inverse[tri];faces=faces[(faces[:,0]!=faces[:,1])&(faces[:,1]!=faces[:,2])&(faces[:,2]!=faces[:,0])]
    edges=np.unique(np.sort(np.concatenate([faces[:,[0,1]],faces[:,[1,2]],faces[:,[2,0]]]),axis=1),axis=0);a,z=edges.T;reference=rest[a]-rest[z];length=np.linalg.norm(reference,axis=1);valid=length>1e-8;a=a[valid];z=z[valid];reference=reference[valid];weights=1/np.maximum(length[valid],.0003)
    free=np.zeros(count,bool);free[inverse[(fw>.1)&(v['w'].max(1)<.98)]]=True
    eligible=np.zeros(count,bool);eligible[inverse[fw>.1]]=True;fixed=np.zeros(count,bool);fixed[inverse[nails]]=True
    fw_unique=np.bincount(inverse,weights=fw)/counts;gripfaces=faces[np.all(fw_unique[faces]>.5,axis=1)]
    def gap(point):
        location,normal,_,distance=tree.find_nearest(Vector(point));return float((Vector(point)-location).dot(normal)),np.array(normal),np.array(location)
    for face in gripfaces:
        if gap(x[face].mean(0))[0]<.0002:free[face]=True
    # Untangle the few web triangles whose old mixed influences over-compress
    # when the knuckles are placed for a human grip.
    area=lambda p:np.linalg.norm(np.cross(p[tri[:,1]]-p[tri[:,0]],p[tri[:,2]]-p[tri[:,0]]),axis=1)
    ratio=area(goal)/np.maximum(area(base),1e-20);free[inverse[tri[ratio<.45]].ravel()]=True
    expanded=free.copy();expanded[a[free[z]]]=True;expanded[z[free[a]]]=True;free=expanded&eligible&~fixed
    ids=np.flatnonzero(free);lookup=np.full(count,-1,int);lookup[ids]=np.arange(len(ids));diag=np.bincount(np.r_[a,z],weights=np.r_[weights,weights],minlength=count)
    lap=np.diag(diag[ids]);both=free[a]&free[z];np.add.at(lap,(lookup[a[both]],lookup[z[both]]),-weights[both]);np.add.at(lap,(lookup[z[both]],lookup[a[both]]),-weights[both])
    constant=np.zeros((count,3));sel=free[a]&~free[z];np.add.at(constant,a[sel],weights[sel,None]*anchors[z[sel]]);sel=free[z]&~free[a];np.add.at(constant,z[sel],weights[sel,None]*anchors[a[sel]])
    spring=diag[ids]*.04;lap[np.diag_indices(len(ids))]+=spring;inv=np.linalg.inv(lap)
    print('Relaxing',len(ids),'welded skin vertices; fixed nail vertices',int(nails.sum()),flush=True)
    history=[]
    for iteration in range(24):
        edge=x[a]-x[z];cov=np.zeros((count,3,3));term=np.einsum('ni,nj->nij',edge,reference)*weights[:,None,None];np.add.at(cov,a,term);np.add.at(cov,z,term)
        u,_,vt=np.linalg.svd(cov);negative=np.linalg.det(u@vt)<0;u[negative,:,-1]*=-1;rotation=u@vt
        contribution=np.einsum('nij,nj->ni',(rotation[a]+rotation[z])*.5,reference)*weights[:,None]
        rhs=np.zeros((count,3));np.add.at(rhs,a,contribution);np.add.at(rhs,z,-contribution)
        x[ids]=inv@(rhs[ids]+constant[ids]+spring[:,None]*anchors[ids])
        for _ in range(3):
            for i in ids:
                distance,normal,location=gap(x[i])
                if distance<.00010:x[i]+=normal*(.00010-distance)
            # Prevent a coarse triangle from crossing the bar between its
            # vertices even when all three vertices happen to be outside.
            for face in gripfaces:
                moving=face[free[face]]
                if not len(moving):continue
                distance,normal,_=gap(x[face].mean(0))
                if distance<.00006:x[moving]+=normal*(.00006-distance)*3/len(moving)
        history.append(float(np.linalg.norm(x[ids]-anchors[ids],axis=1).max()))
    result=x[inverse];result[nails]=goal[nails]
    np.savez(O/(name+'.npz'),points=result,delta=data['delta'],transforms=data['transforms'])
    metadata=json.loads((O/(seed+'.json')).read_text());metadata['skinRelaxation']={'method':'ARAP with frozen nails and shafts, closed-handle contact constraints','freeWeldedVertices':len(ids),'iterations':len(history),'maximumMoveGameUnits':max(history),'nailVertices':int(nails.sum())};(O/(name+'.json')).write_text(json.dumps(metadata,indent=2))
    measure(name);print('Skin correction saved',flush=True)

def polish(seed='relaxed-grip',name='final-grip'):
    import bmesh
    v,b,ss,d,bones=load();data=np.load(O/(seed+'.npz'));goal=data['points'];tri=np.concatenate(ss)
    w=next(w for w in d['weapons'] if w['name']=='Native weapon Handle')
    mesh=bpy.data.meshes.new('Contact polish proxy');mesh.from_pydata(w['positions'],[],np.array(w['triangles']).reshape(-1,3).tolist());bm=bmesh.new();bm.from_mesh(mesh)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-6);bmesh.ops.holes_fill(bm,edges=[e for e in bm.edges if e.is_boundary],sides=0);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));tree=BVHTree.FromBMesh(bm);bm.free();bpy.data.meshes.remove(mesh)
    image=bpy.data.images.load(str(R/'assets/V1/textures/body-albedo.png'),check_existing=True);pixels=np.empty(image.size[0]*image.size[1]*4,np.float32);image.pixels.foreach_get(pixels);pixels=pixels.reshape(image.size[1],image.size[0],4);uv=v['uv']%1;rgb=pixels[(uv[:,1]*(image.size[1]-1)).astype(int),(uv[:,0]*(image.size[0]-1)).astype(int),:3]
    fw=np.where((v['ids']>=4)&(v['ids']<24),v['w'],0).sum(1);nails=(fw>.5)&(rgb[:,1]>rgb[:,0]*1.4)&(rgb[:,2]>rgb[:,0]*1.3)
    _,ix=np.unique(np.round(v['p'],6),axis=0,return_inverse=True);n=ix.max()+1;counts=np.bincount(ix);x=np.column_stack([np.bincount(ix,weights=goal[:,i])/counts for i in range(3)])
    faces=ix[tri];eligible=np.bincount(ix,weights=fw)/counts>.5;fixed=np.zeros(n,bool);fixed[ix[nails]]=True;free=eligible&~fixed
    faces=faces[np.all(eligible[faces],axis=1)];edges=np.unique(np.sort(np.concatenate([faces[:,[0,1]],faces[:,[1,2]],faces[:,[2,0]]]),axis=1),axis=0);a,z=edges.T;degree=np.bincount(np.r_[a,z],minlength=n);anchors=x.copy()
    # Taubin pairs round the low-poly joint ridges without a one-way shrinking pass.
    for iteration in range(16):
        for factor in (.38,-.39):
            total=np.zeros_like(x);np.add.at(total,a,x[z]);np.add.at(total,z,x[a]);delta=(total/np.maximum(degree,1)[:,None]-x)*factor
            x[free]+=delta[free]
        move=x-anchors;length=np.linalg.norm(move,axis=1);x=anchors+move*np.minimum(1,.0012/np.maximum(length,1e-20))[:,None]
    samples=[(1.,0.,0.),(0.,1.,0.),(0.,0.,1.),(1/3,1/3,1/3)]
    for t in (.2,.4,.6,.8):samples.extend([(t,1-t,0),(t,0,1-t),(0,t,1-t)])
    for iteration in range(12):
        changed=0
        for face in faces:
            for bary in samples:
                bary=np.array(bary);point=bary@x[face];loc,normal,_,_=tree.find_nearest(Vector(point));gap=(Vector(point)-loc).dot(normal)
                if gap>=.00015:continue
                influence=bary*free[face];den=float(influence@influence)
                if den<1e-12:continue
                x[face]+=influence[:,None]*np.array(normal)[None,:]*(.00015-gap)/den;changed+=1
        if changed==0:break
    result=x[ix];result[nails]=goal[nails]
    np.savez(O/(name+'.npz'),points=result,delta=data['delta'],transforms=data['transforms'])
    info=json.loads((O/(seed+'.json')).read_text());info['contactPolish']={'barycentricSamples':len(samples),'marginMeters':.00015,'iterations':iteration+1,'fixedNailVertices':int(nails.sum()),'maxMoveMeters':float(np.linalg.norm(result-goal,axis=1).max())};(O/(name+'.json')).write_text(json.dumps(info,indent=2));measure(name)

if __name__=='__main__':initial()
