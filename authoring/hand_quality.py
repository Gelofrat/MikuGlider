"""Offline corrective deformation gates. These do not certify hand anatomy."""
import numpy as np


DEFAULT_LIMITS = {
    'minimumTriangleAreaRatio': 0.35,
    'minimumEdgeLengthRatio': 0.5,
    'maximumEdgeLengthRatio': 2.0,
    'maximumCorrectiveJumpOverPalmWidth': 0.02,
}


def surface_metrics(before, after, triangles):
    before, after = np.asarray(before, float), np.asarray(after, float)
    triangles = np.asarray(triangles, int)
    if before.shape != after.shape or before.ndim != 2 or before.shape[1] != 3:
        raise ValueError('Expected matching Nx3 surfaces')
    if not np.isfinite(before).all() or not np.isfinite(after).all():
        raise ValueError('Non-finite surface')
    if triangles.ndim != 2 or triangles.shape[1] != 3 or not len(triangles):
        raise ValueError('Empty or invalid triangle coverage')
    if triangles.min() < 0 or triangles.max() >= len(before):
        raise ValueError('Triangle index outside surface')
    a, b = before[triangles], after[triangles]
    na = np.cross(a[:, 1]-a[:, 0], a[:, 2]-a[:, 0])
    nb = np.cross(b[:, 1]-b[:, 0], b[:, 2]-b[:, 0])
    aa, ab = np.linalg.norm(na, axis=1), np.linalg.norm(nb, axis=1)
    valid = aa > 1e-10
    if not valid.any():
        raise ValueError('No nondegenerate baseline triangles')
    ratio = np.where(valid, ab / np.maximum(aa, 1e-30), np.inf)
    edges = np.unique(np.sort(np.concatenate([triangles[:, [0, 1]],
        triangles[:, [1, 2]], triangles[:, [2, 0]]]), axis=1), axis=0)
    ea = np.linalg.norm(before[edges[:, 1]]-before[edges[:, 0]], axis=1)
    eb = np.linalg.norm(after[edges[:, 1]]-after[edges[:, 0]], axis=1)
    edge_valid = ea > 1e-10
    er = eb[edge_valid] / ea[edge_valid]
    worst = int(np.argmin(ratio))
    return {
        'minimumTriangleAreaRatio': float(ratio[worst]),
        'minimumEdgeLengthRatio': float(er.min()),
        'maximumEdgeLengthRatio': float(er.max()),
        'minimumEdge': edges[edge_valid][int(np.argmin(er))].tolist(),
        'maximumEdge': edges[edge_valid][int(np.argmax(er))].tolist(),
        'newDegenerateTriangleFrames': int(np.sum(valid & (ab <= 1e-10))),
        'baselineDegenerateTriangles': int(np.sum(~valid)),
        # Relative to the SAME posed surface with correction disabled. This is
        # a corrective fold diagnostic, not a rest-space normal comparison.
        'largeNormalRotationTriangleFrames': int(np.sum(valid & (np.sum(na*nb, axis=1) <= 0))),
        'worstTriangle': triangles[worst].tolist(),
    }


def failures(row, limits=None):
    limits = DEFAULT_LIMITS if limits is None else limits
    issues = []
    if row.get('frames', 0) <= 0 or row.get('clips', 0) <= 0:
        issues.append('No animation coverage')
    for name, threshold in limits.items():
        field = name
        value = row.get(field)
        if value is None or not np.isfinite(value):
            issues.append('Missing or non-finite '+field)
        elif (value < threshold if name.startswith('minimum') else value > threshold):
            issues.append(f'{field}={value:.6g}, limit={threshold}')
    for field in ('newDegenerateTriangleFrames', 'baselineDegenerateTriangles'):
        if row.get(field) != 0:
            issues.append(f'{field}={row.get(field, "missing")}')
    return issues
