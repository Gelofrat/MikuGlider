"""CPU counterpart of grouped shape weights and renderer-only finger rotations."""
import runpy
from pathlib import Path
import numpy as np
from mathutils import Matrix, Quaternion
R=Path(__file__).resolve().parents[1]
T=runpy.run_path(str(R/'authoring/thumb_grip_tools.py'))
BA=runpy.run_path(str(R/'authoring/bake_thumb_grip_review.py'))
PARENTS=[x['parentBoneIndex'] for x in BA['reference'][0]['bones']]

def apply_weights(v,profile):
    v=v.copy()
    for mesh in profile['meshes']:
        for edit in mesh.get('weights',[]):
            i=edit['vertex'];value=edit['value'];v['ids'][i]=[value['boneIndex'+str(k)] for k in range(4)];v['w'][i]=[value['weight'+str(k)] for k in range(4)]
    return v

def evaluate(v,b,bones,profile,var,weapon,anchor_matrix=None):
    v=apply_weights(v,profile)
    poses=[q for m in profile['meshes'] for q in m['poses'] if q.get('weapon',m.get('weapon','')) in ('',weapon)]
    weights=[BA['activation'](q,bones) for q in poses];totals={}
    for q,w in zip(poses,weights):totals[q.get('group','')]=totals.get(q.get('group',''),0)+w
    weights=[w/max(1,totals[q.get('group','')]) for q,w in zip(poses,weights)]
    offsets=[Quaternion() for _ in range(len(bones))];p=v['p'].astype(float).copy()
    for q,w in zip(poses,weights):
        if q['vertices']:p[q['vertices']]+=np.array([[d[k] for k in ('x','y','z')] for d in q['deltas']])*w
        for edit in q.get('boneRotations',[]):
            r=edit['rotation'];rotation=Quaternion((r['w'],r['x'],r['y'],r['z']))
            if edit.get('absolute'):
                i=edit['bone'];parent=PARENTS[i];source=Matrix((np.linalg.inv(bones[parent])@bones[i]).tolist()).to_quaternion().normalized();rotation=source.inverted()@rotation
            offsets[edit['bone']]=offsets[edit['bone']]@Quaternion().slerp(rotation,w)
    posed=bones.copy()
    for i in range(len(bones)):
        parent=PARENTS[i];m=bones[i] if parent is None or parent<0 else posed[parent]@np.linalg.inv(bones[parent])@bones[i]
        posed[i]=m@np.array(offsets[i].to_matrix().to_4x4())
        if anchor_matrix is not None:
            for pose,weight in zip(poses,weights):
                anchor=pose.get('gripAnchor')
                if not anchor or anchor['bone']!=i or weight==0:continue
                position=np.array([anchor['position'][k] for k in ('x','y','z')]);position=(anchor_matrix@np.r_[position,1])[:3]
                scale=np.linalg.norm(bones[i,:3,:3],axis=0);distance=np.linalg.norm(position-bones[i,:3,3])/max(.0001,scale[0]);t=np.clip((distance-anchor['innerDistance'])/(anchor['outerDistance']-anchor['innerDistance']),0,1);weight*=1-t*t*(3-2*t)
                q=anchor['rotation'];target=Matrix(anchor_matrix.tolist()).to_quaternion().normalized()@Quaternion((q['w'],q['x'],q['y'],q['z']));source=Matrix(bones[i].tolist()).to_quaternion().normalized();rotation=source.slerp(target,weight)
                posed[i,:3,:3]=np.array(rotation.to_matrix())*scale[None,:];posed[i,:3,3]=bones[i,:3,3]*(1-weight)+position*weight
    mat,_=T['blend'](v,b,posed,var)
    return np.einsum('nij,nj->ni',mat,np.c_[p,np.ones(len(p))])[:,:3],posed,weights
