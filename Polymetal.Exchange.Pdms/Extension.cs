using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Aveva.Pdms.Geometry;
using Aveva.Pdms.Maths.Geometry;
using Aveva.Pdms.Utilities.Messaging;
using Tekla.Structures.Geometry3d;

namespace Polymetal.Exchange.Pdms
{
    static class Extension
    {
        public static readonly string DecimalSeparator = NumberFormatInfo.CurrentInfo.CurrencyDecimalSeparator;

        internal static Orientation CreateOrientation(this Vector vectorX, Vector vectorY)
        {
            var axisZ = vectorX.Cross(vectorY);
            if (Math.Abs(vectorX.GetAngleBetween(vectorY)*180/Math.PI - 90) > 0.001)
            {
                vectorY = axisZ.Cross(vectorX);
                axisZ = vectorX.Cross(vectorY);
            }
            axisZ.Normalize();
            var dirY = vectorY.ConvertToDirection().ToString();
            var dirZ = axisZ.ConvertToDirection().ToString();
            return Orientation.Create("Y is " + dirY + " and Z is " + dirZ);
        }

        internal static Direction ConvertToDirection(this Vector vector)
        {
            var v = D3Vector.Create(vector.X, vector.Y, vector.Z);
            var v2 = D3Vector.Create(vector.X, vector.Y, vector.Z);
            v2.ScaleBy(10.0);
            var resDirection = Direction.Create(Position.Create(v.X, v.Y, v.Z), Position.Create(v2.X, v2.Y, v2.Z));
            return resDirection;
            //округление углов, после которого съехали некоторые sctn
            //var direction = resDirection.ToString();
            //var pattern = @"(\d+\.\d{2,})";
            //var regex = new Regex(pattern);
            //var match = regex.Match(direction);
            //var newDirection = direction;
            //while (match.Success)
            //{
            //    var value = match.Groups[1].Value;
            //    var newValue = value;
            //    double doubleValue;
            //    if (Double.TryParse(value.Replace(".", DecimalSeparator).Replace(",", DecimalSeparator), out doubleValue))
            //    {
            //        if (doubleValue < 3)
            //            newValue = "0";
            //        else if (doubleValue > 87)
            //            newValue = "90";
            //        else
            //            newValue = Math.Round(doubleValue, 1).ToString().Replace(",",".");
            //    }

            //    newDirection = newDirection.Replace(value, newValue);
            //    match = match.NextMatch();
            //}
            //Direction tDir;
            //PdmsMessage message;
            //if (Direction.Parse(newDirection, out tDir, out message)) 
            //    return tDir;
            //Console.WriteLine("can't convert {0}", newDirection);
            // return resDirection;

            //return Direction.Create(Position.Create(v.X, v.Y, v.Z), Position.Create(v2.X, v2.Y, v2.Z));
        }

        internal static Orientation ConvertToOrientation(this CoordinateSystem cs)
        {
            var axisZ = cs.AxisX.Cross(cs.AxisY);
            axisZ.Normalize();
            var dirY = cs.AxisY.ConvertToDirection().ToString();
            var dirZ = axisZ.ConvertToDirection().ToString();
            return Orientation.Create("Y is " + dirY + " and Z is " + dirZ);
        }

        internal static CoordinateSystem ConvertToCoordinateSystem(this Orientation ori, Position pos)
        {
            return new CoordinateSystem(pos.ConvertToPoint(), ori.XDir().ConvertToVector(), ori.YDir().ConvertToVector());
        }

        internal static Point ConvertToPoint(this Position pos)
        {
            return new Point(pos.X, pos.Y, pos.Z);
        }

        internal static Position ConvertToPosition(this Point point)
        {
            return Position.Create(point.X, point.Y, point.Z);
        }

        internal static Vector ConvertToVector(this Direction dir)
        {
            return new Vector(dir.East, dir.North, dir.Up);
        }  
    }
}
