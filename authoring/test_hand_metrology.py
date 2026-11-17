"""Analytic fixtures for section measurement and interior contact, run in Blender."""
import runpy,json
from pathlib import Path
import numpy as np
R=Path(__file__).resolve().parents[1];A=runpy.run_path(str(R/'authoring/audit_hand_surface.py'))
p=np.array([[-1,-.5,0],[1,-.5,0],[1,.5,0],[-1,.5,0],[-1,-.5,4],[1,-.5,4],[1,.5,4],[-1,.5,4]],float)
tri=np.array([[0,1,2],[0,2,3],[4,6,5],[4,7,6],[0,4,5],[0,5,1],[1,5,6],[1,6,2],[2,6,7],[2,7,3],[3,7,4],[3,4,0]])
width=A['section_width'];assert abs(width(p,tri,p[0]*0,np.array([0,0,4]))-1)<1e-10
q=np.array([[0,0,1],[1,0,0],[0,1,0]],float);offset=np.array([4,7,-2.])
assert abs(width(p@q.T*.3+offset,tri,offset,np.array([0,0,4])@q.T*.3+offset)-.3)<1e-10
cross=np.array([[-1,-1,0],[1,-1,0],[0,1,0],[0,-.5,-1],[0,-.5,1],[0,.5,.5]],float)
faces=np.array([[0,1,2],[3,4,5]])
assert len(A['intersections'](cross,faces,np.arange(6)))==1
cross[3:,0]+=3;assert A['intersections'](cross,faces,np.arange(6))==[]
(R/'authoring/hand-repair/metrology-tests.json').write_text(json.dumps(dict(tests=4,passed=True,checks=['analytic cuboid width','rotation translation and 0.3 scale','interior intersection','separated surfaces']),indent=2))
print('Four analytic metrology checks passed')
