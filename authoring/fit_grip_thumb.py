"""Reposition the thumb opposition around the user's compact grip references."""
import runpy,json,copy
from pathlib import Path
import numpy as np
R=Path(__file__).resolve().parents[1];F=runpy.run_path(str(R/'authoring/fit_v1_plate_grip.py'));O=F['O']

def fit_exact():
    A=runpy.run_path(str(R/'authoring/audit_hand_surface.py'))
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    v,b,ss,d,bones=F['load']();data=np.load(O/'accepted-grip.npz');info=json.loads((O/'accepted-grip.json').read_text());controls=info['controls'];p0=data['points'];tri=np.concatenate(ss);posed=data['transforms']@np.linalg.inv(b);oldmat,_=F['T']['blend'](v,b,posed,'v1');rest=np.linalg.solve(oldmat,np.c_[p0,np.ones(len(v))][:,:,None])[:,:,0]
    thumb=np.where(v['ids']>=24,v['w'],0).sum(1);tt=tri[np.any(thumb[tri]>.05,axis=1)];other=tri[np.any(thumb[tri]<.95,axis=1)];weld=np.unique(np.round(v['p'],6),axis=0,return_inverse=True)[1];distal=np.where(v['ids']==26,v['w'],0).sum(1);pad=np.flatnonzero(distal>.9);inv=np.linalg.inv(bones[3]);oldcenter=(inv@np.r_[posed[25,:3,3],1])[:3];desiredz=oldcenter[2]+.006
    initial=np.r_[controls['thumb'][0],controls['thumb'][1][2],controls['thumb'][2][2]];q=initial.copy();bounds=np.c_[initial-30,initial+30];bounds[3]=[25,85];bounds[4]=[25,85]
    oldtip=(np.c_[p0[pad],np.ones(len(pad))]@inv.T)[:,:3].mean(0);desiredtip=oldtip[2]-.002
    handtree=BVHTree.FromPolygons(p0.tolist(),tri[np.all(thumb[tri]<.1,axis=1)].tolist(),all_triangles=True)
    def evaluate(q,details=False):
        controls['thumb'][0]=q[:3].tolist();controls['thumb'][1][2]=float(q[3]);controls['thumb'][2][2]=float(q[4]);t=F['deform'](b,bones,controls);mat,_=F['T']['blend'](v,b,t@np.linalg.inv(b),'v1');p=np.einsum('nij,nj->ni',mat,rest)[:,:3];pairs=A['intersections'](p,tt,weld,other)
        center=(inv@((t@np.linalg.inv(b))[25]@np.array([0,0,0,1])))[:3];protrusion=((center[2]-desiredz)*300)**2
        distances=np.sort([handtree.find_nearest(Vector(p[i]))[3]*1000 for i in pad]);contact=float(np.mean((distances[:6]-.25)**2));tip=(np.c_[p[pad],np.ones(len(pad))]@inv.T)[:,:3].mean(0);reach=((tip[2]-desiredtip)*300)**2*4;comfort=.008*np.sum((q-initial)**2);score=len(pairs)*20+contact+protrusion+reach+comfort
        return (score,p,t,{'thumbIntersectionPairs':len(pairs),'contact':contact,'protrusion':protrusion,'reach':reach,'jointControls':q.tolist()}) if details else score
    best=evaluate(q);print('Initial score',best,flush=True)
    for step in (10,5,2.5,1.25,.625):
        for iteration in range(6):
            improved=False
            for axis in range(len(q)):
                for sign in (-1,1):
                    candidate=q.copy();candidate[axis]=np.clip(q[axis]+step*sign,*bounds[axis]);score=evaluate(candidate)
                    if score<best-1e-7:q=candidate;best=score;improved=True
            if not improved:break
        print(step,evaluate(q,True)[3],flush=True)
    score,p,t,report=evaluate(q,True);info['thumbFit']=report;np.savez(O/'thumb-exact.npz',points=p,transforms=t,delta=data['delta']);(O/'thumb-exact.json').write_text(json.dumps(info,indent=2));F['render']('thumb-exact',('game','palm'))

def finalize():
    import bpy
    A=runpy.run_path(str(R/'authoring/audit_hand_surface.py'));v,b,ss,d,bones=F['load']();data=np.load(O/'thumb-exact.npz');p=data['points'];tri=np.concatenate(ss);thumb=np.where(v['ids']>=24,v['w'],0).sum(1);fw=np.where(v['ids']>=4,v['w'],0).sum(1);ht=tri[np.any(fw[tri]>.05,axis=1)];_,ix=np.unique(np.round(v['p'],6),axis=0,return_inverse=True);n=ix.max()+1;counts=np.bincount(ix);x=np.column_stack([np.bincount(ix,weights=p[:,i])/counts for i in range(3)]);anchors=x.copy();faces=ix[tri];edges=np.unique(np.sort(np.concatenate([faces[:,[0,1]],faces[:,[1,2]],faces[:,[2,0]]]),axis=1),axis=0);a,z=edges.T;degree=np.bincount(np.r_[a,z],minlength=n)
    posed=data['transforms']@np.linalg.inv(b);base=posed[24,:3,3];end=posed[25,:3,3];axis=end-base;parameter=np.clip((p-base)@axis/(axis@axis),0,1);distance=np.linalg.norm(p-base-parameter[:,None]*axis,axis=1);near=np.exp(-(distance/.006)**2);long=np.where((v['ids']>=4)&(v['ids']<24),v['w'],0).sum(1);proximal=np.where((v['ids']==24)|(v['ids']==25),v['w'],0).sum(1);joint=thumb*np.exp(-(np.linalg.norm(p-posed[26,:3,3],axis=1)/.0042)**4);weight=np.maximum(np.maximum(proximal,near),joint);weight[long>.1]=0
    image=bpy.data.images.load(str(R/'assets/V1/textures/body-albedo.png'),check_existing=True);pixels=np.empty(image.size[0]*image.size[1]*4,np.float32);image.pixels.foreach_get(pixels);pixels=pixels.reshape(image.size[1],image.size[0],4);uv=v['uv']%1;rgb=pixels[(uv[:,1]*(image.size[1]-1)).astype(int),(uv[:,0]*(image.size[0]-1)).astype(int),:3];nail=(rgb[:,1]>rgb[:,0]*1.4)&(rgb[:,2]>rgb[:,0]*1.3);weight[nail]=0;mask=np.bincount(ix,weights=weight)/counts
    for iteration in range(30):
        for factor in (.5,-.35):
            total=np.zeros_like(x);np.add.at(total,a,x[z]);np.add.at(total,z,x[a]);x+=(total/np.maximum(degree,1)[:,None]-x)*factor*mask[:,None]
        move=x-anchors;length=np.linalg.norm(move,axis=1);x=anchors+move*np.minimum(1,.0015/np.maximum(length,1e-20))[:,None]
    for fraction in (1.,.75,.5,.25,0.):
        candidate=p+(x[ix]-p)*fraction;pairs=A['intersections'](candidate,ht,ix);hit=F['Q']['C']['contact'](candidate,tri[np.all(fw[tri]>.8,axis=1)],d['weapons'])
        print('Thenar',fraction,'self',len(pairs),'weapon',hit,flush=True)
        if not pairs and hit['interiorTrianglePairs']==0:break
    (O/'thenar-check.json').write_text(json.dumps({'self':len(pairs),'selfFaces':[(ht[a].tolist(),ht[z].tolist()) for a,z in pairs],'weapon':hit},default=lambda x:x.item() if isinstance(x,np.generic) else x))
    assert not pairs and hit['interiorTrianglePairs']==0
    info=json.loads((O/'thumb-exact.json').read_text());info['thenarSmoothing']={'fraction':fraction,'maxMoveMeters':float(np.linalg.norm(candidate-p,axis=1).max()),'allHandSelfIntersectionPairs':len(pairs),'allFingerWeaponCrossings':hit['interiorTrianglePairs']};print(info['thenarSmoothing'],flush=True)
    np.savez(O/'release-grip.npz',points=candidate,transforms=data['transforms'],delta=data['delta']);(O/'release-grip.json').write_text(json.dumps(info,indent=2));F['profile']('release-grip');F['render']('release-grip',('game','palm','dorsal'))

if __name__=='__main__':fit_exact();finalize()
