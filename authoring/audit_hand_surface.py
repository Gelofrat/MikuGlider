"""Measured surface QA in Blender. No claim of universal anatomical certification."""
import runpy,json,hashlib
from pathlib import Path
import numpy as np
from mathutils.bvhtree import BVHTree
R=Path(__file__).resolve().parents[1]
T=runpy.run_path(str(R/'authoring/thumb_grip_tools.py'));M=T['M']
BA=runpy.run_path(str(R/'authoring/bake_thumb_grip_review.py'))
O=R/'authoring/hand-repair'
interior_crossing=runpy.run_path(str(R/'authoring/thumb_contact.py'))['interior_crossing']

def section_width(points,tri,a,z):
 """Intersect triangles with a plane normal to a bone, then measure its width.
 Uses actual triangle/plane intersections, not vertex count/density.
 """
 axis=z-a;axis=axis/np.linalg.norm(axis);center=(a+z)/2
 q=points[tri];d=(q-center)@axis;hits=[]
 for i,j in ((0,1),(1,2),(2,0)):
  mask=(d[:,i]*d[:,j]<0)&(np.abs(d[:,i]-d[:,j])>1e-12)
  t=d[mask,i]/(d[mask,i]-d[mask,j]);hits.extend(q[mask,i]+t[:,None]*(q[mask,j]-q[mask,i]))
 if len(hits)<3:return None
 u=np.cross(axis,[1,0,0] if abs(axis[0])<.9 else [0,1,0]);u/=np.linalg.norm(u);v=np.cross(axis,u)
 xy=(np.array(hits)-center)@np.array([u,v]).T
 # Feret minimum of a convex hull occurs normal to one of its edges.
 pts=sorted(set(map(tuple,xy)))
 def cross(o,a,b):return (a[0]-o[0])*(b[1]-o[1])-(a[1]-o[1])*(b[0]-o[0])
 lo=[];hi=[]
 for p in pts:
  while len(lo)>=2 and cross(lo[-2],lo[-1],p)<=0:lo.pop()
  lo.append(p)
 for p in reversed(pts):
  while len(hi)>=2 and cross(hi[-2],hi[-1],p)<=0:hi.pop()
  hi.append(p)
 hull=np.array(lo[:-1]+hi[:-1]);edges=np.roll(hull,-1,axis=0)-hull
 normals=np.c_[-edges[:,1],edges[:,0]];normals/=np.linalg.norm(normals,axis=1)[:,None]
 return float(np.ptp(xy@normals.T,axis=0).min())

def intersections(p,tri,weld,other=None):
 tree=BVHTree.FromPolygons(p.tolist(),tri.tolist(),all_triangles=True,epsilon=0)
 full=tri if other is None else other
 target=tree if other is None else BVHTree.FromPolygons(p.tolist(),full.tolist(),all_triangles=True,epsilon=0)
 pairs=tree.overlap(target);unique=[];seen=set()
 for a,b in pairs:
  if (other is None and a>=b) or set(weld[tri[a]])&set(weld[full[b]]):continue
  pair=tuple(sorted((tuple(sorted(tri[a])),tuple(sorted(full[b])))))
  if pair in seen:continue
  seen.add(pair)
  qa,qb=p[tri[a]],p[full[b]]
  na=np.cross(qa[1]-qa[0],qa[2]-qa[0]);nb=np.cross(qb[1]-qb[0],qb[2]-qb[0])
  la,lb=np.linalg.norm(na),np.linalg.norm(nb)
  if min(la,lb)<1e-12:continue
  da=(qb-qa[0])@(na/la);db=(qa-qb[0])@(nb/lb)
  # Ignore coplanar/tangent contact and numeric seam contact (20 micrometres
  # in world units). Both faces must cross the other's plane in their interior.
  if not interior_crossing(qa,qb):continue
  unique.append([int(a),int(b)])
 return unique

def audit(variant,side,candidate=True,full_collisions=False):
 path=M['PATHS'][variant];raw=(O/(variant+'.mikumesh')).read_bytes() if candidate else (R/path).read_bytes()
 _,v,b,ss=M['read'](M['chunks'](raw)['arm-'+side]);si=0 if side=='left' else 1
 thumb=np.where(v['ids']>=24,v['w'],0).sum(1);tri=np.concatenate(ss);fulltri=tri.copy()
 tri=tri[np.any(thumb[tri]>.05,axis=1)];rest=np.linalg.inv(b)
 # Anatomical sections isolate triangles belonging to the corresponding bone
 # region; neighboring fingers must not inflate the reported thumb width.
 sections=[]
 for bone in (25,26):
  weight=np.where(v['ids']==bone,v['w'],0).sum(1)
  sections.append(tri[np.any(weight[tri]>.25,axis=1)])
 # Robot end bones are not anatomical skin landmarks on the human replacement.
 # Anchor each section at the midpoint of the actual weighted tissue extent,
 # then carry that SAME rest plane through the bone's skinning transform.
 # Extent uses triangle vertices (not vertex-density averages).
 sectionPlanes=[]
 for tr,bone in zip(sections,(25,26)):
  axis=rest[bone+1,:3,3]-rest[bone,:3,3];axis/=np.linalg.norm(axis)
  extent=v['p'][np.unique(tr)]@axis
  center=rest[bone,:3,3]+axis*((float(extent.min())+float(extent.max()))*.5-rest[bone,:3,3]@axis)
  sectionPlanes.append((center-axis*.01,center+axis*.01))
 restWidths=[section_width(v['p'],tr,*plane) for tr,plane in zip(sections,sectionPlanes)]
 weld=np.unique(np.round(v['p'],6),axis=0,return_inverse=True)[1]
 row=dict(variant=variant,side=side,candidate=candidate,sha256=hashlib.sha256(raw).hexdigest(),clips=len(BA['clips']),frames=0,
          restSectionWidths=restWidths,minimumSectionRatios=[1.,1.],missingSections=0,
          sectionMethod='tissue-extent midpoint in rest bone coordinates; fixed plane follows skinning transform',
          maximumWeightSumError=float(np.abs(v['w'].sum(1)-1).max()),
          negativeWeights=int((v['w']<0).sum()),invalidBoneIndices=int(((v['ids']<0)|(v['ids']>=len(b))).sum()),
          collisionSamples=[],worstSections=[None,None],anatomyStatus='requires_visual_review',
          allFrameThumbHandIntersections=full_collisions,maximumInteriorIntersectionPairs=0,intersectionFrames=0,
          limitations=['Cross section is convex-envelope minimum width; it does not detect cavities.',
                      'Interior intersections exclude adjacent/coplanar/tangent pairs and penetrations below 20 micrometres; only listed poses are tested.',
                      'Native clip exports do not cover live transition interruptions or every weapon.',
                      'No human anatomical range of motion is inferred from robot bone Euler angles.'])
 worstPoints=[None,None];worstBones=[None,None]
 profile=json.loads((O/(variant+'-corrections.json') if candidate else (R/path).parent/'corrections.json').read_text())
 row['correctionsSha256']=hashlib.sha256((O/(variant+'-corrections.json') if candidate else (R/path).parent/'corrections.json').read_bytes()).hexdigest()
 row['clipCatalogSha256']=hashlib.sha256((R/'simulation/Reports/cycler-thumb-review/native-clips/clips.json').read_bytes()).hexdigest()
 row['clipBonesSha256']=hashlib.sha256((R/'simulation/Reports/cycler-thumb-review/native-clips/clip-bones.bin').read_bytes()).hexdigest()
 poses=[p for m in profile['meshes'] for p in m['poses']] if side=='left' else []
 if any(p.get('contact') for p in poses):raise ValueError('Experimental contact controller is not accepted')
 for clip in BA['clips']:
  name='Cycler thumb grip' if clip['label'].startswith('SMG ') or clip['key'].startswith('weapon-smg-') else 'Shotgun thumb grip' if clip['label'].startswith('Shotgun ') or clip['key'].startswith('weapon-bounce-shotgun-') else 'Plate thumb opposition'
  pose=next((p for p in poses if p['name']==name),None)
  delta=np.zeros((len(v),3))
  if pose is not None:delta[pose['vertices']]=[[d['x'],d['y'],d['z']] for d in pose['deltas']]
  for f in range(clip['offset'],clip['offset']+clip['count']):
   bones=BA['native'][f,si*28:si*28+28].astype(float)
   if side=='left' or variant=='v2':mat,_=T['blend'](v,b,bones,variant if side=='left' else 'v2')
   else:
    native=bones@b;mat=sum(native[v['ids'][:,k]]*v['w'][:,k,None,None] for k in range(4))
   weight=BA['activation'](pose,bones) if pose is not None else 0
   p=v['p']+delta*weight
   points=np.einsum('nij,nj->ni',mat,np.c_[p,np.ones(len(v))])[:,:3]
   row['frames']+=1
   if full_collisions:
    pairs=intersections(points,tri,weld,fulltri)
    if pairs:row['intersectionFrames']+=1
    if len(pairs)>row['maximumInteriorIntersectionPairs']:
     row['maximumInteriorIntersectionPairs']=len(pairs)
     row['worstIntersection']=dict(clip=clip['key'],frame=f-clip['offset'],pairs=pairs[:20])
     np.savez_compressed(O/f'{variant}-{side}-{candidate}-intersection.npz',points=points,thumbTriangles=tri,triangles=fulltri,bones=bones,pairs=np.array(pairs))
   for k,(tr,bone) in enumerate(zip(sections,(25,26))):
    transform=bones[bone]@b[bone]
    plane=(np.c_[np.array(sectionPlanes[k]),np.ones(2)]@transform.T)[:,:3]
    width=section_width(points,tr,*plane)
    if width is None or restWidths[k] is None:row['missingSections']+=1;continue
    # Remove rig scale; the game renders arms at 0.3 of authoring size.
    scale=np.linalg.norm(transform[:3,:3],axis=0)
    if np.ptp(scale)>1e-5:
     rigid=(np.c_[v['p'],np.ones(len(v))]@transform.T)[:,:3]
     expected=section_width(rigid,tr,*plane)
    else:expected=restWidths[k]*float(scale.mean())
    if expected is None or expected<=1e-10:row['missingSections']+=1;continue
    ratio=width/expected
    if ratio<row['minimumSectionRatios'][k]:
     row['minimumSectionRatios'][k]=ratio;worstPoints[k]=points.copy();worstBones[k]=bones.copy()
     row['worstSections'][k]=dict(clip=clip['key'],frame=f-clip['offset'],timeSeconds=(f-clip['offset'])*clip['duration']/(clip['count']-1),width=width)
  if clip['key'] in ('clip-367','clip-381','clip-313'):
   pairs=intersections(points,tri,weld);row['collisionSamples'].append(dict(clip=clip['key'],frame=clip['count']-1,pairs=len(pairs),firstPairs=pairs[:12]))
 for k,points in enumerate(worstPoints):
  if points is not None:
   pairs=intersections(points,tri,weld);row['collisionSamples'].append(dict(section=k,**row['worstSections'][k],pairs=len(pairs),firstPairs=pairs[:12]))
   np.savez_compressed(O/f'{variant}-{side}-{candidate}-worst{k}.npz',points=points,triangles=tri,bones=worstBones[k])
 (O/f'{variant}-{side}-{candidate}-audit.json').write_text(json.dumps(row,indent=2))
 print(json.dumps({k:row[k] for k in ('variant','side','candidate','frames','minimumSectionRatios','missingSections','collisionSamples')}))
 return row
