using System;
using Xbim.Common.Geometry;
using Xbim.Ifc2x3.GeometricModelResource;
using Xbim.Ifc2x3.GeometryResource;

namespace Polymetal.Pdms.IfcServer
{
    public static class DoubleExtension
    {
        public static double Round(this double input)
        {
            return Math.Round(input, 4);
        }
    }

    public static class XbimPointExtension
    {
        public static XbimPoint3D Round(this XbimPoint3D input)
        {
            return new XbimPoint3D(Math.Round(input.X, 4), Math.Round(input.Y, 4), Math.Round(input.Z, 4));
        }
    }
    public static class XbimVectorExtension
    {
        public static XbimVector3D Round(this XbimVector3D input)
        {
            return new XbimVector3D(Math.Round(input.X, 4), Math.Round(input.Y, 4), Math.Round(input.Z, 4));
        }
    }

    public static class XbimMatrixExtension
    {
        public static XbimMatrix3D Round(this XbimMatrix3D input)
        {
            return new XbimMatrix3D(
                Math.Round(input.M11, 4), Math.Round(input.M12, 4), Math.Round(input.M13, 4), Math.Round(input.M14, 4),
                Math.Round(input.M21, 4), Math.Round(input.M22, 4), Math.Round(input.M23, 4), Math.Round(input.M24, 4), 
                Math.Round(input.M31, 4), Math.Round(input.M32, 4), Math.Round(input.M33, 4), Math.Round(input.M34, 4),
                Math.Round(input.OffsetX, 4), Math.Round(input.OffsetY, 4), Math.Round(input.OffsetZ, 4), Math.Round(input.M44, 4)
                );
        }

        public static int GetHash(this XbimMatrix3D input)
        {
            input = input.Round();
            var str = string.Format("XbimMatrix3D-{0}-{1}-{2}-{3}-{4}-{5}-{6}-{7}-{8}-{9}-{10}-{11}-{12}-{13}-{14}-{15}",
                input.M11, input.M12, input.M13, input.M14,
                input.M21, input.M22, input.M23, input.M24,
                input.M31, input.M32, input.M33, input.M34,
                input.OffsetX, input.OffsetY, input.OffsetZ, input.M44);
            return str.GetHashCode();
        }
    }
    public static class IfcRepresentationItemExtension
    {
        public static IfcRepresentationItem Copy(this IfcRepresentationItem input)
        {
            IfcRepresentationItem output;
            if (input is IfcFacetedBrep)
            {
                var obj = (IfcFacetedBrep) input;
                var outputObj = input.Model.Instances.New<IfcFacetedBrep>();
                outputObj.Outer = obj.Outer;
                output = outputObj;
            }
            else if (input is IfcExtrudedAreaSolid)
            {
                var obj = (IfcExtrudedAreaSolid)input;
                var outputObj = input.Model.Instances.New<IfcExtrudedAreaSolid>();
                outputObj.Depth = obj.Depth;
                outputObj.ExtrudedDirection = obj.ExtrudedDirection;
                outputObj.SweptArea = obj.SweptArea;
                outputObj.Position = obj.Position;
                output = outputObj;
            }
            else if (input is IfcRevolvedAreaSolid)
            {
                var obj = (IfcRevolvedAreaSolid)input;
                var outputObj = input.Model.Instances.New<IfcRevolvedAreaSolid>();
                outputObj.Angle = obj.Angle;
                outputObj.Axis = obj.Axis;
                outputObj.SweptArea = obj.SweptArea;
                outputObj.Position = obj.Position;
                output = outputObj;
            }
            else if (input is IfcBooleanClippingResult)
            {
                var obj = (IfcBooleanClippingResult)input;
                var outputObj = input.Model.Instances.New<IfcBooleanClippingResult>();
                outputObj.FirstOperand = obj.FirstOperand;
                outputObj.SecondOperand = obj.SecondOperand;
                outputObj.Operator = obj.Operator;
                output = outputObj;
            }
            else if (input is IfcBooleanResult)
            {
                var obj = (IfcBooleanResult)input;
                var outputObj = input.Model.Instances.New<IfcBooleanResult>();
                outputObj.FirstOperand = obj.FirstOperand;
                outputObj.SecondOperand = obj.SecondOperand;
                outputObj.Operator = obj.Operator;
                output = outputObj;
            }
            else
            {
                throw new NotImplementedException();
            }
            return output;
        }
    }
    
}
