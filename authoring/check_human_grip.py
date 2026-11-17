"""Check the authored Plate grip against complete captured native matrices."""
import runpy,json,hashlib
from pathlib import Path
import numpy as np
R=Path(__file__).resolve().parents[1];F=runpy.run_path(str(R/'authoring/fit_v1_plate_grip.py'));O=F['O'];Q=F['Q'];E=runpy.run_path(str(R/'authoring/pose_corrections.py'));A=runpy.run_path(str(R/'authoring/audit_hand_surface.py'))

def run():
    v,b,ss,d,bones=F['load']();tri=np.concatenate(ss);profile=json.loads((O/'v1-corrections.json').read_text());old=json.loads((O/'original/v1-corrections.json').read_text());fw=np.where(v['ids']>=4,v['w'],0).sum(1);ft=tri[np.all(fw[tri]>.8,axis=1)];allhand=tri[np.any(fw[tri]>.05,axis=1)]
    ix=np.unique(np.round(v['p'],6),axis=0,return_inverse=True)[1];reports=[];first=json.loads((F['CAP']/'frames.jsonl').read_text().splitlines()[0]);local=[]
    for w in d['weapons']:
        name=w['name'].removeprefix('Native weapon ');rec=next((x for x in first['character']['renderers'] if x['role']=='weapon' and x['path'].split('/')[-1]==name),None)
        if rec is None:continue
        mat=np.array(rec['localToWorld']).reshape(4,4);mat[:3,3]-=d['origin'];local.append((name,w,(np.c_[w['positions'],np.ones(len(w['positions']))]@np.linalg.inv(mat).T)[:,:3]))
    area=lambda p:np.linalg.norm(np.cross(p[ft[:,1]]-p[ft[:,0]],p[ft[:,2]]-p[ft[:,0]]),axis=1)
    for capture in ('20261116-180944-759-d974ee','20261116-162906-805-0aca39'):
        records=[json.loads(line) for line in (F['CAP'].parent/capture/'frames.jsonl').read_text().splitlines()];row={'capture':capture,'frames':0,'candidateContactFrames':0,'originalContactFrames':0,'maximumCandidateCrossings':0,'maximumOriginalCrossings':0,'newDegenerateFaces':0,'candidateSelfIntersectionFrames':0,'maximumSelfIntersectionPairs':0,'selfIntersectionSamples':[],'contactRows':[]};cache={}
        for i,frame in enumerate(records):
            ch=frame['character'];rec=next(r for r in ch['renderers'] if r['role']=='arm_left');native=np.array(rec.get('sourceBoneMatrices') or rec['boneMatrices']).reshape(-1,4,4)[:28];origin=np.array(ch['camera']['position']);native[:,:3,3]-=origin
            handle=np.array(next(r for r in ch['renderers'] if r['path'].endswith('/Handle'))['localToWorld']).reshape(4,4);handle[:3,3]-=origin
            p,posed,weights=E['evaluate'](v,b,native,profile,'v1','YAPlateLauncher',handle);before=E['evaluate'](v,b,native,old,'v1','YAPlateLauncher')[0];weapons=[]
            for name,w,points in local:
                rec=next(r for r in ch['renderers'] if r['role']=='weapon' and r['path'].split('/')[-1]==name);mat=np.array(rec['localToWorld']).reshape(4,4);mat[:3,3]-=origin;weapons.append(dict(w,positions=(np.c_[points,np.ones(len(points))]@mat.T)[:,:3]))
            hit=Q['C']['contact'](p,ft,weapons)['interiorTrianglePairs'];oldhit=Q['C']['contact'](before,ft,weapons)['interiorTrianglePairs'];row['frames']+=1;row['candidateContactFrames']+=int(hit>0);row['originalContactFrames']+=int(oldhit>0);row['maximumCandidateCrossings']=max(hit,row['maximumCandidateCrossings']);row['maximumOriginalCrossings']=max(oldhit,row['maximumOriginalCrossings']);row['newDegenerateFaces']+=int(((area(before)>1e-10)&(area(p)<=1e-10)).sum());row['contactRows'].append({'frame':i,'candidate':hit,'original':oldhit,'activation':weights[-1]})
            z=A['intersections'](p,allhand,ix);row['candidateSelfIntersectionFrames']+=int(len(z)>0);row['maximumSelfIntersectionPairs']=max(row['maximumSelfIntersectionPairs'],len(z))
            if i in (0,85,100,120,150,210,240,270,330,len(records)-1):
                a=A['intersections'](before,allhand,ix);row['selfIntersectionSamples'].append({'frame':i,'original':len(a),'candidate':len(z)})
            if i%100==0:print(capture,i,hit,oldhit,flush=True)
        reports.append(row);(O/'recorded-contact-checks.json').write_text(json.dumps(reports,indent=2));print({k:v for k,v in row.items() if k not in ('contactRows','selfIntersectionSamples')},flush=True)
    return reports

if __name__=='__main__':run()
