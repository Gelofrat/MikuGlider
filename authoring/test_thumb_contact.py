import unittest,runpy
from pathlib import Path
import numpy as np
crossing=runpy.run_path(str(Path(__file__).with_name('thumb_contact.py')))['interior_crossing']
class ContactTests(unittest.TestCase):
 def setUp(self):self.a=np.array([[-1,-1,0],[1,-1,0],[0,1,0]],float)
 def test_crossing(self):self.assertTrue(crossing(self.a,[[0,0,-1],[0,1,1],[0,-1,1]]))
 def test_overlapping_bounds_and_straddling_planes_are_not_enough(self):self.assertFalse(crossing(self.a,[[0,.8,-1],[0,2,1],[0,2,-1]]))
 def test_touch(self):self.assertFalse(crossing(self.a,[[0,1,0],[0,2,1],[0,2,-1]]))
 def test_coplanar_contact(self):self.assertFalse(crossing(self.a,self.a*.5))
 def test_rigid_transform_and_scale(self):
  q=np.array([[0,0,1],[1,0,0],[0,1,0]],float);offset=np.array([.2,-.5,.7]);b=np.array([[0,0,-1],[0,1,1],[0,-1,1]],float)
  self.assertTrue(crossing(self.a@q*.3+offset,b@q*.3+offset))
if __name__=='__main__':unittest.main()
