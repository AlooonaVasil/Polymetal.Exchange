using System;
using System.Globalization;
using Aveva.Core.Database;
using Aveva.Core.Geometry;
using Aveva.Core.Maths.Geometry;
using Polymetal.Core.Common;
using Tekla.Structures.Geometry3d;

namespace Polymetal.Exchange.E3D
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


        /// <summary>
        /// Перемещение элемента в иерархии, если его класс изменился
        /// </summary>
        /// <param name="sctn">элемент</param>
        /// <param name="root">новый владелец</param>
        /// <returns></returns>
        internal static bool CheckOwnerOrMove(this DbElement item, DbElement root)
        {
            if (item.Owner == root) 
                return true;

            if (!item.IsInsertable(root)) 
                return false;

            item.InsertAfterLast(root);

            return true;
        }

        internal static bool SetAttributeIfChanged(this DbElement item, DbAttribute attribute, object value)
        {
            if (attribute == DbAttributeInstance.NAME)
            {
                var strValue = (string)value;
                if (string.IsNullOrEmpty(strValue))
                {
                    if (item.GetBool(DbAttributeInstance.ISNAME))
                        item.SetAttributeDefault(attribute);

                    return true;
                }

                if (!strValue.StartsWith("/"))
                    strValue = $"/{strValue}";

                if (item.GetBool(DbAttributeInstance.ISNAME))
                {
                    if (item.GetString(DbAttributeInstance.NAME) == strValue)
                        return true;

                    item.SetAttributeDefault(attribute);
                }

                var itemWithName = MDB.CurrentMDB.FindElement(DbType.Design, strValue);
                if (itemWithName.IsNull || itemWithName.IsDeleted)
                {
                    item.SetAttribute(attribute, strValue);
                    return true;
                }

                return false;
            }

            if (attribute == DbAttributeInstance.DESC ||
                attribute == UserAttributes.PolyMagiGuid ||
                attribute == UserAttributes.PolyRevitGuid ||
                attribute == UserAttributes.PolyTeklaGuid ||
                attribute == DbAttributeInstance.FUNC ||
                attribute.Type == DbAttributeType.STRING)
            {
                var strValue = (string)value;
                if (attribute == DbAttributeInstance.DESC && strValue.Length > DbAttributeInstance.DESC.MaximumSize)
                {
                    strValue = strValue.Substring(0, DbAttributeInstance.DESC.MaximumSize);
                }

                var cValue = item.GetString(attribute);
                if (!cValue.Equals(strValue))
                    item.SetAttribute(attribute, strValue);

                return true;
            }

            if (attribute == UserAttributes.PolyTeklaClass)
            {
                var cValue = 0;
                item.GetValidInteger(attribute, ref cValue);
                if (int.TryParse(value.ToString(), out var nValue) && cValue != nValue)
                    item.SetAttribute(attribute, nValue);

                return true;
            }

            if (attribute == DbAttributeInstance.POS)
            {
                var cValue = item.GetPosition(attribute);
                if (!cValue.Equals((Position)value))
                    item.SetAttribute(attribute, (Position)value);

                return true;
            }

            if (attribute == DbAttributeInstance.ORI)
            {
                var cValue = item.GetOrientation(attribute);
                if (!cValue.Equals((Orientation)value))
                    item.SetAttribute(attribute, (Orientation)value);

                return true;
            }

            if (attribute == DbAttributeInstance.HEIG ||
                attribute == DbAttributeInstance.DIAM ||
                attribute == DbAttributeInstance.XLEN ||
                attribute == DbAttributeInstance.YLEN ||
                attribute == DbAttributeInstance.ZLEN ||
                attribute == DbAttributeInstance.XBOT ||
                attribute == DbAttributeInstance.YBOT ||
                attribute == DbAttributeInstance.XTOP ||
                attribute == DbAttributeInstance.YTOP || 
                attribute.Type== DbAttributeType.DOUBLE)
            {
                var cValue = item.GetDouble(attribute);
                if (!cValue.Equals((double)value))
                    item.SetAttribute(attribute, (double)value);

                return true;
            }

            if (attribute == DbAttributeInstance.LEVE)
            {
                var cValue = item.GetIntegerArray(attribute);
                if (!cValue.Equals((int[])value))
                    item.SetAttribute(attribute, (int[])value);

                return true;
            }

            if (attribute == DbAttributeInstance.VXREF)
            {
                var cValue = item.GetElementArray(attribute);
                var nValue = (DbElement[])value;
                if (nValue.Length > 500)
                    return false;

                if (cValue.Length != nValue.Length)
                {
                    item.SetAttribute(attribute, nValue);
                }
                else
                {
                    for (var i = 0; i < cValue.Length; i++)
                    {
                        if (cValue[i].Equals(nValue[i]))
                            continue;

                        item.SetAttribute(attribute, nValue);
                        break;
                    }
                }

                return true;
            }

            if (attribute == DbAttributeInstance.CATR)
            {
                var cValue = item.GetElement(attribute);
                if (!cValue.Equals((DbElement)value))
                    item.SetAttribute(attribute, (DbElement)value);

                return true;
            }

            throw new NotImplementedException();
        }
    }
}
