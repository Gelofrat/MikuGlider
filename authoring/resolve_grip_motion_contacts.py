"""Bake small skin clearances against recorded moving weapon surfaces."""
import bpy,bmesh,runpy,json
from pathlib import Path
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
R=Path(__file__).resolve().parents[1];F=runpy.run_path(str(R/'authoring/fit_v1_plate_grip.py'));E=runpy.run_path(str(R/'authoring/pose_corrections.py'));A=runpy.run_path(str(R/'authoring/audit_hand_surface.py'));O=F['O']

def build():
    v,b,ss,d,bones=F['load']();data=np.load(O/'release-grip.npz');goal=data['points'];p=goal.copy();tri=np.concatenate(ss);fw=np.where(v['ids']>=4,v['w'],0).sum(1);ft=tri[np.all(fw[tri]>.8,axis=1)];ht=tri[np.any(fw[tri]>.05,axis=1)];weld=np.unique(np.round(v['p'],6),axis=0,return_inverse=True)[1];profile=json.loads((O/'v1-corrections.json').read_text());frames=[json.loads(l) for l in (F['CAP']/'frames.jsonl').read_text().splitlines()];local=[]
    for w in d['weapons']:
        name=w['name'].removeprefix('Native weapon ');rec=next(r for r in frames[0]['character']['renderers'] if r['role']=='weapon' and r['path'].split('/')[-1]==name);mat=np.array(rec['localToWorld']).reshape(4,4);mat[:3,3]-=d['origin'];local.append((name,w,(np.c_[w['positions'],np.ones(len(w['positions']))]@np.linalg.inv(mat).T)[:,:3]))
    obstacles=[];free=np.zeros(len(v),bool);canonical=data['transforms']@np.linalg.inv(b)
    for i in (92,96,197,272,280,283):
        ch=frames[i]['character'];rec=next(r for r in ch['renderers'] if r['role']=='arm_left');native=np.array(rec.get('sourceBoneMatrices') or rec['boneMatrices']).reshape(-1,4,4)[:28];origin=np.array(ch['camera']['position']);native[:,:3,3]-=origin;handle=np.array(next(r for r in ch['renderers'] if r['path'].endswith('/Handle'))['localToWorld']).reshape(4,4);handle[:3,3]-=origin;frame_points,posed,_=E['evaluate'](v,b,native,profile,'v1','YAPlateLauncher',handle);mapping=canonical[3]@np.linalg.inv(posed[3]);residual=(np.c_[frame_points,np.ones(len(v))]@mapping.T)[:,:3]-goal
        for name,w,points in local:
            rec=next(r for r in ch['renderers'] if r['role']=='weapon' and r['path'].split('/')[-1]==name);mat=np.array(rec['localToWorld']).reshape(4,4);mat[:3,3]-=origin;wp=(np.c_[points,np.ones(len(points))]@(mapping@mat).T)[:,:3];weapon=dict(w,positions=wp);hit=F['Q']['C']['contact'](p+residual,ft,[weapon])
            if not hit['interiorTrianglePairs']:continue
            faces=ft[hit['handTriangles']];free[faces.ravel()]=True;mesh=bpy.data.meshes.new('Moving weapon clearance');mesh.from_pydata(wp.tolist(),[],np.array(w['triangles']).reshape(-1,3).tolist());bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-6);bmesh.ops.holes_fill(bm,edges=[e for e in bm.edges if e.is_boundary],sides=0);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));
            if bm.calc_volume(signed=True)<0:bmesh.ops.reverse_faces(bm,faces=list(bm.faces))
            tree=BVHTree.FromBMesh(bm);bm.free();bpy.data.meshes.remove(mesh);obstacles.append((i,name,tree,weapon,residual));print('Obstacle',i,name,hit,flush=True)
    image=bpy.data.images.load(str(R/'assets/V1/textures/body-albedo.png'),check_existing=True);pixels=np.empty(image.size[0]*image.size[1]*4,np.float32);image.pixels.foreach_get(pixels);pixels=pixels.reshape(image.size[1],image.size[0],4);uv=v['uv']%1;rgb=pixels[(uv[:,1]*(image.size[1]-1)).astype(int),(uv[:,0]*(image.size[0]-1)).astype(int),:3];nails=(rgb[:,1]>rgb[:,0]*1.4)&(rgb[:,2]>rgb[:,0]*1.3)&(fw>.5)
    edges=np.unique(np.sort(np.concatenate([tri[:,[0,1]],tri[:,[1,2]],tri[:,[2,0]]]),axis=1),axis=0);a,z=edges.T;expanded=free.copy();expanded[a[free[z]]]=True;expanded[z[free[a]]]=True;free=expanded&(fw>.5)&~nails
    maximum=np.zeros(weld.max()+1,bool);np.logical_or.at(maximum,weld,free);free=maximum[weld]&~nails
    samples=[np.array(x) for x in ((1.,0,0),(0.,1,0),(0.,0,1),(1/3,1/3,1/3))]
    for t in (.2,.4,.6,.8):samples.extend([np.array([t,1-t,0]),np.array([t,0,1-t]),np.array([0,t,1-t])])
    for iteration in range(30):
        for frame,name,tree,weapon,residual in obstacles:
            hit=F['Q']['C']['contact'](p+residual,ft,[weapon]);faces=ft[hit['handTriangles']]
            for face in faces:
                for bary in samples:
                    point=bary@(p[face]+residual[face]);loc,normal,_,_=tree.find_nearest(Vector(point));gap=(Vector(point)-loc).dot(normal);influence=bary*free[face];den=influence@influence
                    if gap<.00012 and den>1e-12:p[face]+=influence[:,None]*np.array(normal)[None,:]*(.00012-gap)/den
        # Keep duplicated UV seam vertices together after projections.
        counts=np.bincount(weld);p=np.column_stack([np.bincount(weld,weights=p[:,i])/counts for i in range(3)])[weld]
        move=p-goal;length=np.linalg.norm(move,axis=1);p=goal+move*np.minimum(1,.0035/np.maximum(length,1e-20))[:,None]
    hits=[{'frame':i,'part':n,'crossings':F['Q']['C']['contact'](p+r,ft,[w])['interiorTrianglePairs']} for i,n,t,w,r in obstacles];self=A['intersections'](p,ht,weld);resthit=F['Q']['C']['contact'](p,ft,d['weapons'])['interiorTrianglePairs'];report={'obstacles':hits,'selfCrossings':len(self),'staticWeaponCrossings':resthit,'maxMoveMeters':float(np.linalg.norm(p-goal,axis=1).max())};(O/'motion-clearance-report.json').write_text(json.dumps(report,indent=2));print(report,flush=True)
    if self or resthit or any(row['crossings'] for row in hits):return
    np.savez(O/'motion-grip.npz',points=p,transforms=data['transforms'],delta=data['delta']);info=json.loads((O/'release-grip.json').read_text());info['motionClearance']=report;(O/'motion-grip.json').write_text(json.dumps(info,indent=2));F['profile']('motion-grip')
