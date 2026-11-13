using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xbim.Common.Geometry;
using Xbim.Ifc;
using Xbim.Ifc2x3.Extensions;
using Xbim.Ifc2x3.GeometricModelResource;
using Xbim.Ifc2x3.GeometryResource;
using Xbim.Ifc2x3.PresentationAppearanceResource;
using Xbim.Ifc2x3.ProductExtension;
using Xbim.Ifc2x3.ProfileResource;

namespace Polymetal.Exchange.Geometry.Helpers
{
    internal class HatchingGenerator
    {
        private static IfcStore _modelStore;

        internal static void ModifyRepresentation(IfcStore modelStore, List<IfcElement> collection, List<Utilities.DTO.IfcElementInformation> elementInformationList)
        {
            _modelStore = modelStore;

            foreach (var ifcElement in collection)
            {
                if (ifcElement is IfcOpeningElement)
                    continue;

                var elementInformation = elementInformationList.FirstOrDefault(x => x.Id == ifcElement.Tag.ToString());
                if (elementInformation == null || elementInformation.HatchLines == null || elementInformation.HatchLines.Count == 0)
                    continue;

                ModifyRepresentation(ifcElement, elementInformation.HatchLines);

                elementInformation.HatchLines = null;
            }
        }

        private static void ModifyRepresentation(IfcElement ifcElement, List<Utilities.DTO.HatchLine> hatchLines)
        {
            if (ifcElement == null || ifcElement.Representation == null || ifcElement.Representation.Representations == null)
                return;

            var ifcRepresentation = ifcElement.Representation.Representations.FirstOrDefault();
            if (ifcRepresentation == null)
                return;

            var matrix = ifcElement.Transform();
            matrix.Invert();

            var presentationStyleAssigment = ifcRepresentation.Items.FirstOrDefault().StyledByItem.FirstOrDefault().Styles.FirstOrDefault();
            foreach (var hatchLine in hatchLines)
            {
                var startPoint = matrix.Transform(new XbimPoint3D(hatchLine.Start.X, hatchLine.Start.Y, hatchLine.Start.Z));
                var endPoint = matrix.Transform(new XbimPoint3D(hatchLine.End.X, hatchLine.End.Y, hatchLine.End.Z));

                using (var tr = _modelStore.BeginTransaction("update representation"))
                {
                    var representationItem = GetHatchLineRepresentation(startPoint, endPoint);
                    if (representationItem != null)
                    {
                        CreateStyledItem(representationItem, presentationStyleAssigment);
                        ifcRepresentation.Items.Add(representationItem);
                    }

                    tr.Commit();
                }
            }
        }

        private static IfcRepresentationItem GetHatchLineRepresentation(XbimPoint3D startPoint, XbimPoint3D endPoint)
        {
            var direction = (endPoint - startPoint).Normalized();

            var axis2Placement3D = CreateAxis2Placement3D(startPoint, direction);

            var rectangleProfileDef = CreateRectangleProfileDef(1.0, 1.0);

            return CreateExtrudedAreaSolid(axis2Placement3D, rectangleProfileDef, new XbimVector3D(0, 0, 1),
                (endPoint - startPoint).Length);
        }

        private static void CreateStyledItem(IfcRepresentationItem representationItem, IfcPresentationStyleAssignment presentationStyleAssignment)
        {
            var styledItem = _modelStore.Instances.New<IfcStyledItem>();
            styledItem.Item = representationItem;
            styledItem.Styles.Add(presentationStyleAssignment);
        }

        private static IfcRectangleProfileDef CreateRectangleProfileDef(double xlen, double ylen)
        {
            var axis2Placement2D = CreateAxis2Placement2D(0.0, ylen / 2.0);

            var rectangleProfileDef = _modelStore.Instances.New<IfcRectangleProfileDef>();
            rectangleProfileDef.ProfileType = IfcProfileTypeEnum.AREA;
            rectangleProfileDef.XDim = xlen;
            rectangleProfileDef.YDim = ylen;
            rectangleProfileDef.Position = axis2Placement2D;

            return rectangleProfileDef;
        }

        private static IfcAxis2Placement2D CreateAxis2Placement2D(double x, double y)
        {
            var axis2Placement2D = _modelStore.Instances.New<IfcAxis2Placement2D>();
            axis2Placement2D.Location = CreateCartesianPoint2(x, y);
            axis2Placement2D.RefDirection = CreateDirection2(1.0, 0.0);

            return axis2Placement2D;
        }

        private static IfcCartesianPoint CreateCartesianPoint2(double x, double y)
        {
            var cartesianPoint = _modelStore.Instances.New<IfcCartesianPoint>();
            cartesianPoint.SetXY(x, y);

            return cartesianPoint;
        }

        private static IfcDirection CreateDirection2(double x, double y)
        {
            var direction = _modelStore.Instances.New<IfcDirection>();
            direction.SetXY(x, y);

            return direction;
        }

        private static IfcAxis2Placement3D CreateAxis2Placement3D(XbimPoint3D point, XbimVector3D axis)
        {
            var axis2Placement3D = _modelStore.Instances.New<IfcAxis2Placement3D>();
            axis2Placement3D.Location = CreateCartesianPoint(point);
            axis2Placement3D.Axis = CreateDirection(axis);

            var refDirection = XbimVector3D.CrossProduct(axis, Math.Abs(axis.Z) < 0.9 ? new XbimVector3D(0, 0, 1) : new XbimVector3D(1, 0, 0)).Normalized();
            axis2Placement3D.RefDirection = CreateDirection(refDirection);

            return axis2Placement3D;
        }

        private static IfcCartesianPoint CreateCartesianPoint(XbimPoint3D point)
        {
            var cartesianPoint = _modelStore.Instances.New<IfcCartesianPoint>();
            cartesianPoint.X = point.X;
            cartesianPoint.Y = point.Y;
            cartesianPoint.Z = point.Z;

            return cartesianPoint;
        }

        private static IfcDirection CreateDirection(XbimVector3D vector)
        {
            var direction = _modelStore.Instances.New<IfcDirection>();
            direction.X = vector.X;
            direction.Y = vector.Y;
            direction.Z = vector.Z;

            return direction;
        }

        private static IfcExtrudedAreaSolid CreateExtrudedAreaSolid(IfcAxis2Placement3D axis2Placement3D, IfcProfileDef profileDef, XbimVector3D extrudeDirection, double depth)
        {
            var extrudedAreaSolid = _modelStore.Instances.New<IfcExtrudedAreaSolid>();
            extrudedAreaSolid.Depth = depth < 1.0 ? 1.0 : depth;
            extrudedAreaSolid.ExtrudedDirection = CreateDirection(extrudeDirection);
            extrudedAreaSolid.SweptArea = profileDef;
            extrudedAreaSolid.Position = axis2Placement3D;

            return extrudedAreaSolid;
        }
    }
}
