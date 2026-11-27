#nullable enable

using System;
using System.Numerics;

namespace Panopticon.Client;

/// <summary>
/// Least-squares unit plane normal of a small sample set (the tyre support points),
/// double precision throughout: cyclic Jacobi eigendecomposition of the scatter
/// matrix, eigenvector of the smallest eigenvalue, Z forced positive so the normal
/// faces up. Returns false when the samples define no unique plane.
/// </summary>
internal static class WheelPlaneFit
{
    internal static bool TryGetNormal(ReadOnlySpan<Vector3> points, out Vector3 normal)
    {
        normal = Vector3.UnitZ;

        if (points.Length < 3)
        {
            return false;
        }

        double mx = 0d;
        double my = 0d;
        double mz = 0d;

        foreach (var point in points)
        {
            mx += point.X;
            my += point.Y;
            mz += point.Z;
        }

        mx /= points.Length;
        my /= points.Length;
        mz /= points.Length;

        var scatter = new double[3, 3];

        foreach (var point in points)
        {
            var dx = point.X - mx;
            var dy = point.Y - my;
            var dz = point.Z - mz;
            scatter[0, 0] += dx * dx;
            scatter[0, 1] += dx * dy;
            scatter[0, 2] += dx * dz;
            scatter[1, 1] += dy * dy;
            scatter[1, 2] += dy * dz;
            scatter[2, 2] += dz * dz;
        }

        scatter[1, 0] = scatter[0, 1];
        scatter[2, 0] = scatter[0, 2];
        scatter[2, 1] = scatter[1, 2];

        var (eigenValues, eigenVectors) = JacobiEigen(scatter);
        var smallest = 0;

        for (var i = 1; i < 3; i++)
        {
            if (eigenValues[i] < eigenValues[smallest])
            {
                smallest = i;
            }
        }

        // A unique plane needs the smallest eigenvalue strictly below the others: a
        // collinear or coincident sample set has a second vanishing eigenvalue and no
        // well-defined normal.
        var largest = Math.Max(eigenValues[0], Math.Max(eigenValues[1], eigenValues[2]));
        var secondSmallest = double.MaxValue;

        for (var i = 0; i < 3; i++)
        {
            if (i != smallest && eigenValues[i] < secondSmallest)
            {
                secondSmallest = eigenValues[i];
            }
        }

        if (largest <= 0d || secondSmallest <= largest * 1e-9d)
        {
            return false;
        }

        var nx = eigenVectors[smallest][0];
        var ny = eigenVectors[smallest][1];
        var nz = eigenVectors[smallest][2];

        if (nz < 0d)
        {
            nx = -nx;
            ny = -ny;
            nz = -nz;
        }

        if (Math.Abs(nz) < 1e-9d)
        {
            normal = new Vector3(0f, 0f, 1f);
            return true;
        }

        var len = Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));

        normal = new Vector3((float)(nx / len), (float)(ny / len), (float)(nz / len));
        return true;
    }

    /// <summary>Cyclic Jacobi eigen-decomposition of a symmetric 3x3 matrix (deterministic).</summary>
    private static (double[] Values, double[][] Vectors) JacobiEigen(double[,] matrix)
    {
        var a = new double[3, 3];
        var v = new double[3, 3];

        for (var i = 0; i < 3; i++)
        {
            for (var j = 0; j < 3; j++)
            {
                a[i, j] = matrix[i, j];
            }

            v[i, i] = 1d;
        }

        for (var sweep = 0; sweep < 64; sweep++)
        {
            var off = Math.Max(Math.Abs(a[0, 1]), Math.Max(Math.Abs(a[0, 2]), Math.Abs(a[1, 2])));

            if (off < 1e-14d)
            {
                break;
            }

            for (var p = 0; p < 2; p++)
            {
                for (var q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(a[p, q]) < 1e-18d)
                    {
                        continue;
                    }

                    var theta = (a[q, q] - a[p, p]) / (2d * a[p, q]);
                    var sign = theta >= 0d ? 1d : -1d;
                    var t = sign / (Math.Abs(theta) + Math.Sqrt((theta * theta) + 1d));
                    var c = 1d / Math.Sqrt((t * t) + 1d);
                    var s = t * c;

                    for (var k = 0; k < 3; k++)
                    {
                        var akp = a[k, p];
                        var akq = a[k, q];
                        a[k, p] = (c * akp) - (s * akq);
                        a[k, q] = (s * akp) + (c * akq);
                    }

                    for (var k = 0; k < 3; k++)
                    {
                        var apk = a[p, k];
                        var aqk = a[q, k];
                        a[p, k] = (c * apk) - (s * aqk);
                        a[q, k] = (s * apk) + (c * aqk);
                    }

                    for (var k = 0; k < 3; k++)
                    {
                        var vkp = v[k, p];
                        var vkq = v[k, q];
                        v[k, p] = (c * vkp) - (s * vkq);
                        v[k, q] = (s * vkp) + (c * vkq);
                    }
                }
            }
        }

        var values = new double[3];
        var vectors = new double[3][];

        for (var i = 0; i < 3; i++)
        {
            values[i] = a[i, i];
            var norm = Math.Sqrt((v[0, i] * v[0, i]) + (v[1, i] * v[1, i]) + (v[2, i] * v[2, i]));
            vectors[i] = norm > 1e-30d
                ? [v[0, i] / norm, v[1, i] / norm, v[2, i] / norm]
                : [1d, 0d, 0d];
        }

        return (values, vectors);
    }
}
