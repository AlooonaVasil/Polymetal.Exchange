using Clipper2Lib;
using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Geometry3d;

namespace Polymetal.Exchange.Tekla.v2021.Helpers
{
    internal class PolygonSplitter
    {
        private const double SCALE = 1e6; // точность до 1e-6
        private const int MAX_VERTICES = 499;

        public static List<List<Point>> SplitPolygon(List<Point> input, Vector normal)
        {
            if (input.Count <= MAX_VERTICES)
            {
                input.Reverse();
                return new List<List<Point>> { input };
            }

            var vx = new Vector(input[1] - input[0]).GetNormal();
            var vz = normal.GetNormal();
            var vy = vz.Cross(vx).GetNormal();

            var coordinateSystem = new CoordinateSystem(input[0], vx, vy);
            var matrixTo = MatrixFactory.ToCoordinateSystem(coordinateSystem);
            var matrixFrom = MatrixFactory.FromCoordinateSystem(coordinateSystem);

            var polygon2D = new List<Point>();
            foreach (var point in input)
            {
                var tp = matrixTo.Transform(point);
                polygon2D.Add(new Point(tp.X, tp.Y, 0.0));
            }

            var parts2D = RecursiveSplit(polygon2D);

            return parts2D.Select(part => part.Select(point => matrixFrom.Transform(point)).ToList()).ToList();
        }

        private static List<List<Point>> RecursiveSplit(List<Point> poly)
        {
            if (poly.Count <= MAX_VERTICES)
                return new List<List<Point>> { poly };

            var poly64 = ToPath64(poly);
            var (minX, minY, maxX, maxY) = GetBounds(poly64);
            long width = maxX - minX;
            long height = maxY - minY;

            var xs = poly.Select(p => p.X).ToArray();
            var ys = poly.Select(p => p.Y).ToArray();
            bool splitByX = width >= height;

            double median = splitByX
                ? xs.OrderBy(x => x).ElementAt(xs.Length / 2)
                : ys.OrderBy(y => y).ElementAt(ys.Length / 2);

            long margin = Math.Max(width, height) * 2;
            long med = (long)(median * SCALE);

            Path64 halfPlane;

            if (splitByX)
            {
                halfPlane = new Path64
            {
                new Point64(minX - margin, minY - margin),
                new Point64(med, minY - margin),
                new Point64(med, maxY + margin),
                new Point64(minX - margin, maxY + margin)
            };
            }
            else
            {
                halfPlane = new Path64
            {
                new Point64(minX - margin, minY - margin),
                new Point64(maxX + margin, minY - margin),
                new Point64(maxX + margin, med),
                new Point64(minX - margin, med)
            };
            }

            var subject = new Paths64 { poly64 };
            var clip = new Paths64 { halfPlane };

            var left = Clipper.Intersect(subject, clip, FillRule.NonZero);
            var right = Clipper.Difference(subject, clip, FillRule.NonZero);

            var result = new List<List<Point>>();

            if (left.Count > 0)
                result.AddRange(RecursiveSplit(FromPath64(left[0])));
            if (right.Count > 0)
                result.AddRange(RecursiveSplit(FromPath64(right[0])));

            return result;
        }

        private static Path64 ToPath64(List<Point> pts)
        {
            return new Path64(pts.Select(p => new Point64((long)(p.X * SCALE), (long)(p.Y * SCALE))));
        }

        private static List<Point> FromPath64(Path64 path)
        {
            return path.Select(p => new Point(p.X / SCALE, p.Y / SCALE)).ToList();
        }

        private static (long minX, long minY, long maxX, long maxY) GetBounds(Path64 path)
        {
            if (path.Count == 0)
                throw new ArgumentException("Path is empty");

            long minX = path[0].X, maxX = path[0].X;
            long minY = path[0].Y, maxY = path[0].Y;

            foreach (var pt in path)
            {
                if (pt.X < minX) minX = pt.X;
                if (pt.X > maxX) maxX = pt.X;
                if (pt.Y < minY) minY = pt.Y;
                if (pt.Y > maxY) maxY = pt.Y;
            }

            return (minX, minY, maxX, maxY);
        }
    }
}
