"""Narrow-phase triangle crossing: compare both plane-intersection intervals."""
import numpy as np

def interior_crossing(a,b,tolerance=2e-5):
 a=np.asarray(a,dtype=float);b=np.asarray(b,dtype=float)
 na=np.cross(a[1]-a[0],a[2]-a[0]);nb=np.cross(b[1]-b[0],b[2]-b[0]);la=np.linalg.norm(na);lb=np.linalg.norm(nb)
 if min(la,lb)<1e-12:return False
 na/=la;nb/=lb;da=(a-b[0])@nb;db=(b-a[0])@na
 if not (da.min()<-tolerance and da.max()>tolerance and db.min()<-tolerance and db.max()>tolerance):return False
 direction=np.cross(na,nb);length=np.linalg.norm(direction)
 if length<1e-10:return False
 direction/=length
 def interval(p,d):
  hits=[p[i] for i in range(3) if abs(d[i])<1e-12]
  for i,j in ((0,1),(1,2),(2,0)):
   if d[i]*d[j]<0:hits.append(p[i]+d[i]/(d[i]-d[j])*(p[j]-p[i]))
  values=np.array(hits)@direction
  return values.min(),values.max()
 aa,az=interval(a,da);ba,bz=interval(b,db)
 return bool(min(az,bz)-max(aa,ba)>tolerance)
