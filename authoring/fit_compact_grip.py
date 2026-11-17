"""Coupled four-finger fitting: a closed grip, not four independent hooks."""
import bpy,bmesh,runpy,json,copy
from pathlib import Path
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
R=Path(__file__).resolve().parents[1];F=runpy.run_path(str(R/'authoring/fit_v1_plate_grip.py'));O=F['O']

def fit(name='compact-fit'):
    v,b,ss,d,bones=F['load']();base=np.array(d['positions']);j=np.linalg.inv(b)[:,:3,3];tri=np.concatenate(ss)
    controls=json.loads((O/'compact-seed.json').read_text())['controls'];controls['metacarpals']={}
    names=['index','middle','ring','pinky'];starts=[4,9,14,19];splays=[20,0,-13,-25]
    w=next(w for w in d['weapons'] if w['name']=='Native weapon Handle');mesh=bpy.data.meshes.new('Coupled grip proxy');mesh.from_pydata(w['positions'],[],np.array(w['triangles']).reshape(-1,3).tolist());bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-6);bmesh.ops.holes_fill(bm,edges=[e for e in bm.edges if e.is_boundary],sides=0);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));tree=BVHTree.FromBMesh(bm);bm.free();bpy.data.meshes.remove(mesh)
    regions=[];pads=[];samples=[];radii=[];scale=np.linalg.norm((bones[3]@b[3])[:3,:3],axis=0).mean()
    for name,start,spread in zip(names,starts,splays):
        controls['metacarpals'][name]=[0,0,spread];controls[name][0]=[90,0,-spread]
        weight=np.where((v['ids']>=start)&(v['ids']<=start+4),v['w'],0).sum(1);ids=np.flatnonzero(weight>.85);regions.append(ids)
        pp=[];ssamples=[];rr=[]
        for bone in range(start+1,start+4):
            u=j[bone+1]-j[bone];length=np.linalg.norm(u);u/=length;t=(v['p']-j[bone])@u;mask=(weight>.9)&(t>length*.25)&(t<length*.8)
            points=v['p'][mask];radius=np.ptp(points[:,2])*.5*scale;pad=np.flatnonzero(mask&(v['p'][:,1]<np.quantile(points[:,1],.4)));pp.append(pad)
            for t in (.25,.5,.75):ssamples.append((bone,j[bone]*(1-t)+j[bone+1]*t));rr.append(radius)
        pads.append(pp);samples.append(ssamples);radii.append(np.array(rr))
    # CMC flexion, MCP flexion, MCP axial rotation, residual splay, PIP, DIP.
    q=np.array([[0,90,0,2,95,60],[0,90,0,0,95,60],[0,88,0,-2,95,60],[0,80,0,-4,95,60]],float)
    limits=np.array([[-12,12],[65,95],[-5,5],[-4,4],[75,108],[40,75]],float)
    native=bones@b;hom=np.c_[v['p'],np.ones(len(v))]
    def evaluate(q,details=False):
        for k,n in enumerate(names):
            cmc,mcp,roll,splay,pip,dip=q[k];controls['metacarpals'][n]=[cmc,0,splays[k]];controls[n]=[[mcp,roll,-splays[k]+splay],[pip,0,0],[dip,0,0]]
        target=F['deform'](b,bones,controls);p=F['surface'](v,b,bones,base,target);points=[];collision=0.;contact=0.
        for k,start in enumerate(starts):
            gaps=[]
            for i in regions[k]:
                loc,normal,_,_=tree.find_nearest(Vector(p[i]));gaps.append((Vector(p[i])-loc).dot(normal)*1000)
            inside=np.maximum(0,.12-np.array(gaps));collision+=10*np.mean(inside**2)+15*np.max(inside)**2
            for ids in pads[k]:
                dist=sorted(tree.find_nearest(Vector(p[i]))[3]*1000 for i in ids);contact+=float(np.mean(np.array(dist[:max(3,len(dist)//3)])**2))
            points.append(np.array([(target[bone]@np.r_[point,1])[:3] for bone,point in samples[k]]))
        packing=0.;reports=[]
        for k in range(3):
            distances=np.linalg.norm(points[k][:,None,:]-points[k+1][None,:,:],axis=2)*1000-(radii[k][:,None]+radii[k+1][None,:])*1000
            gaps=np.r_[distances.min(1),distances.min(0)]
            # Shaft side contacts throughout the curl, rather than just one
            # pair of vertices at the web. Allow longitudinal stagger.
            packing+=12*np.mean(np.maximum(0,gaps-.25)**2)+8*np.mean(np.maximum(0,-gaps-.15)**2)
            reports.append({'pair':names[k]+'-'+names[k+1],'meanCapsuleGapMm':float(gaps.mean()),'maxCapsuleGapMm':float(gaps.max()),'minCapsuleGapMm':float(gaps.min())})
        comfort=.005*np.sum((q[:,5]-.65*q[:,4])**2)+.03*np.sum(q[:,2]**2)+.02*np.sum(q[:,0]**2)
        score=collision+contact+packing+comfort
        return {'cost':score,'collision':collision,'contact':contact,'packing':packing,'pairs':reports} if details else score
    best=evaluate(q);print('Initial',evaluate(q,True),flush=True)
    for step in (8,4,2,1,.5):
        for iteration in range(6):
            improved=False
            for k in range(4):
                for axis in range(6):
                    for sign in (-1,1):
                        candidate=q.copy();candidate[k,axis]=np.clip(candidate[k,axis]+step*sign,*limits[axis]);score=evaluate(candidate)
                        if score<best-1e-7:q=candidate;best=score;improved=True
            if not improved:break
        print('Step',step,evaluate(q,True),flush=True)
    report=evaluate(q,True);F['save_candidate']('compact-fit',copy.deepcopy(controls));(O/'compact-fit-search.json').write_text(json.dumps(report,indent=2));F['render']('compact-fit')

def separate_skin(seed='compact-contact-polished',name='accepted-grip'):
    A=runpy.run_path(str(R/'authoring/audit_hand_surface.py'));v,b,ss,d,bones=F['load']();data=np.load(O/(seed+'.npz'));p=data['points'].copy();original=p.copy();tri=np.concatenate(ss)
    weights=np.array([np.where((v['ids']>=s)&(v['ids']<s+5),v['w'],0).sum(1) for s in (4,9,14,19)]).T
    fw=weights.sum(1);ht=tri[np.any(fw[tri]>.05,axis=1)];weld=np.unique(np.round(v['p'],6),axis=0,return_inverse=True)[1];labels=weights.argmax(1)
    posed=data['transforms']@np.linalg.inv(b);centers=posed[:,:3,3]
    image=bpy.data.images.load(str(R/'assets/V1/textures/body-albedo.png'),check_existing=True);pixels=np.empty(image.size[0]*image.size[1]*4,np.float32);image.pixels.foreach_get(pixels);pixels=pixels.reshape(image.size[1],image.size[0],4);uv=v['uv']%1;rgb=pixels[(uv[:,1]*(image.size[1]-1)).astype(int),(uv[:,0]*(image.size[0]-1)).astype(int),:3];nails=(rgb[:,1]>rgb[:,0]*1.4)&(rgb[:,2]>rgb[:,0]*1.3)&(fw>.5)
    def center_at(finger,point):
        joints=centers[np.arange(5+5*finger,9+5*finger)];a=joints[:-1];u=np.diff(joints,axis=0);t=np.clip(np.sum((point-a)*u,axis=1)/np.sum(u*u,axis=1),0,1);z=a+t[:,None]*u;return z[np.argmin(np.linalg.norm(z-point,axis=1))]
    for iteration in range(40):
        pairs=A['intersections'](p,ht,weld)
        if not pairs:break
        a,z=map(lambda i:ht[i],pairs[0]);fa=int(np.argmax(weights[a].sum(0)));fz=int(np.argmax(weights[z].sum(0)))
        if fa==fz:raise ValueError('Intra-finger fold requires joint repair')
        ca=p[a].mean(0);cz=p[z].mean(0);axis=center_at(fz,cz)-center_at(fa,ca);axis/=np.linalg.norm(axis)
        distance=max(0.,float((p[a]@axis).max()-(p[z]@axis).min()+.00008))*.5
        if distance>.0015:raise ValueError('Side contact requires refitting the bones')
        for ids,finger,sign,center in ((a,fa,-1,ca),(z,fz,1,cz)):
            radius=np.linalg.norm(p-center,axis=1);amount=np.exp(-(radius/.002)**2);amount[(labels!=finger)|(fw<.8)|nails]=0;amount[ids[~nails[ids]]]=1
            # Synchronize UV seam duplicates, including exactly coincident skin.
            maximum=np.zeros(weld.max()+1);np.maximum.at(maximum,weld,amount);amount=maximum[weld]
            p+=amount[:,None]*axis[None,:]*distance*sign
    pairs=A['intersections'](p,ht,weld);contact=F['Q']['C']['contact'](p,tri[np.all(fw[tri]>.8,axis=1)],d['weapons'])
    report={'selfIntersectionPairs':len(pairs),'weaponIntersectionPairs':contact['interiorTrianglePairs'],'iterations':iteration,'maximumSkinMoveMeters':float(np.linalg.norm(p-original,axis=1).max())};print(report,flush=True)
    assert not pairs and contact['interiorTrianglePairs']==0,report
    np.savez(O/(name+'.npz'),points=p,transforms=data['transforms'],delta=data['delta']);info=json.loads((O/(seed+'.json')).read_text());info['sideContact']=report;(O/(name+'.json')).write_text(json.dumps(info,indent=2));F['profile'](name);F['render'](name,('game','palm','dorsal'))

if __name__=='__main__':fit()
