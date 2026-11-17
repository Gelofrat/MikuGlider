"""Current hand validation. Historical verifier: hand-repair/original/verify_thumb_volume.py."""
import runpy
from pathlib import Path
runpy.run_path(str(Path(__file__).with_name('verify_hand_acceptance.py')), run_name='__main__')
