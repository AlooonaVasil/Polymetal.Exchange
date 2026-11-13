using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xbim.Ifc2x3.ActorResource;
using Xbim.Ifc2x3.GeometricConstraintResource;
using Xbim.Ifc2x3.GeometricModelResource;
using Xbim.Ifc2x3.GeometryResource;
using Xbim.Ifc2x3.Kernel;
using Xbim.Ifc2x3.MeasureResource;
using Xbim.Ifc2x3.PresentationAppearanceResource;
using Xbim.Ifc2x3.PresentationOrganizationResource;
using Xbim.Ifc2x3.PresentationResource;
using Xbim.Ifc2x3.ProductExtension;
using Xbim.Ifc2x3.ProfileResource;
using Xbim.Ifc2x3.PropertyResource;
using Xbim.Ifc2x3.RepresentationResource;
using Xbim.Ifc2x3.SharedBldgElements;
using Xbim.Ifc2x3.SharedBldgServiceElements;
using Xbim.Ifc2x3.TopologyResource;

namespace Polymetal.E3D.IfcServer
{
    internal class EntityCreator
    {
        private static IfcStore _modelStore;
        private static IfcGeometricRepresentationContext _modelContext;
        private static IfcProject _project;
        internal static Dictionary<string, object> Entities;

        internal static void Init()
        {
            Entities = new Dictionary<string, object>();
            Representation.Init();
        }

        internal static IfcStore CreateModel()
        {
            var credentials = new XbimEditorCredentials
            {
                ApplicationIdentifier = "DCAD",
                ApplicationFullName = "Export IFC application",
                ApplicationDevelopersName = "DCAD",
                EditorsOrganisationName = "Engineering",
                EditorsFamilyName = "DCAD",
                EditorsGivenName = "DCAD",
                ApplicationVersion = "1.0"
            };

            _modelStore = IfcStore.Create(credentials, IfcSchemaVersion.Ifc2X3, XbimStoreType.InMemoryModel);
            return _modelStore;
        }

        internal static void CreateProject(string projectName)
        {
            _project = _modelStore.Instances.New<IfcProject>();
            _project.Initialize(ProjectUnits.SIUnitsUK);
            _modelStore.CalculateModelFactors();
            _project.Name = projectName;
            _modelContext = _project.ModelContext;
        }

        internal static IfcSite CreateSite(string name, string[] siteMatrix)
        {
            var relativePlacement = CreateAxis2Placement3D(
                new XbimPoint3D(double.Parse(siteMatrix[9]), double.Parse(siteMatrix[10]), double.Parse(siteMatrix[11])),
                new XbimVector3D(double.Parse(siteMatrix[6]), double.Parse(siteMatrix[7]), double.Parse(siteMatrix[8])),
                new XbimVector3D(double.Parse(siteMatrix[0]), double.Parse(siteMatrix[1]), double.Parse(siteMatrix[2])), XbimMatrix3D.Identity);
            var placement = _modelStore.Instances.New<IfcLocalPlacement>();
            placement.RelativePlacement = relativePlacement;

            var site = _modelStore.Instances.New<IfcSite>();
            site.Name = name;
            site.CompositionType = IfcElementCompositionEnum.ELEMENT;
            site.ObjectPlacement = placement;

            //test building
            //site.RefLatitude = new IfcCompoundPlaneAngleMeasure(new List<long> { 42, 24, 53, 508911 });
            //site.RefLongitude = new IfcCompoundPlaneAngleMeasure(new List<long> { -71, -15, -29, -58837 });
            //site.ObjectType = new IfcLabel(string.Empty);
            //site.RefElevation = new IfcLengthMeasure(0);

            _project.AddSite(site);
            return site;
        }

        internal static IfcBuilding CreateBuilding(IfcSite site)
        {
            var relativePlacement = CreateAxis2Placement3D(
                 new XbimPoint3D(0, 0, 0),
                 new XbimVector3D(0.0, 0.0, 1.0), new XbimVector3D(1.0, 0.0, 0.0), XbimMatrix3D.Identity);
            var placement = _modelStore.Instances.New<IfcLocalPlacement>();
            placement.RelativePlacement = relativePlacement;

            var postalAddress = _modelStore.Instances.New<IfcPostalAddress>();

            var building = _modelStore.Instances.New<IfcBuilding>();
            building.Name = "Здание";
            building.CompositionType = IfcElementCompositionEnum.ELEMENT;
            building.BuildingAddress = postalAddress;
            building.ObjectPlacement = placement;
            site.AddBuilding(building);

            //var relAggregates = _modelStore.Instances.New<IfcRelAggregates>();
            //relAggregates.RelatingObject = site;
            //relAggregates.RelatedObjects.Add(building);

            var property = CreateProperty("NumberOfStoreys", 1);
            var pset = CreatePropertySet(new List<IfcProperty> { property });
            CreateRelDefinesByProperties(building, pset);
            return building;
        }

        internal static IfcBuildingStorey CreateBuildingStorey(IfcBuilding building)
        {
            var relativePlacement = CreateAxis2Placement3D(
                 new XbimPoint3D(0, 0, 0),
                 new XbimVector3D(0.0, 0.0, 1.0), new XbimVector3D(1.0, 0.0, 0.0), XbimMatrix3D.Identity);
            var placement = _modelStore.Instances.New<IfcLocalPlacement>();
            placement.RelativePlacement = relativePlacement;

            var buildingStorey = _modelStore.Instances.New<IfcBuildingStorey>();
            buildingStorey.Name = "Этаж 1";
            buildingStorey.ObjectPlacement = placement;
            buildingStorey.CompositionType = IfcElementCompositionEnum.ELEMENT;

            var relAggregates = _modelStore.Instances.New<IfcRelAggregates>();
            relAggregates.RelatingObject = building;
            relAggregates.RelatedObjects.Add(buildingStorey);

            return buildingStorey;
        }

        internal static IfcProduct CreateProduct(string name, string type, IfcRepresentation representation, IfcSite site, Dictionary<string, string> properties)
        {
            IfcProduct product;
            var placement = CreateLocalPlacement(Representation.RootMatrix.Transform(new XbimPoint3D(0.0, 0.0, 0.0)), Representation.RootMatrix.Transform(new XbimVector3D(0.0, 0.0, 1.0)),
                Representation.RootMatrix.Transform(new XbimVector3D(1.0, 0.0, 0.0)), site.ObjectPlacement);
            var productDefinitionShape = CreateProductDefinitionShape(representation);
            type = type.Split(' ').First();
            switch (type)
            {
                case "EQUI":
                    product = _modelStore.Instances.New<IfcEquipmentElement>();
                    break;
                case "HVAC":
                    product = _modelStore.Instances.New<IfcFlowSegment>();
                    break;
                case "PIPE":
                    product = _modelStore.Instances.New<IfcFlowSegment>();
                    break;
                case "SCTN":
                    product = _modelStore.Instances.New<IfcBeam>();
                    break;
                case "GWALL":
                    product = _modelStore.Instances.New<IfcWall>();
                    break;
                case "FLOOR":
                    product = _modelStore.Instances.New<IfcPlate>();
                    break;
                case "PANE":
                    product = _modelStore.Instances.New<IfcPlate>();
                    break;
                case "CWBRAN":
                    product = _modelStore.Instances.New<IfcElectricalElement>();
                    break;
                case "CABLE":
                    product = _modelStore.Instances.New<IfcElectricalElement>();
                    break;
                case "LINDIM":
                    product = _modelStore.Instances.New<IfcAnnotation>();
                    break;
                default:
                    product = _modelStore.Instances.New<IfcBuildingElementProxy>();
                    break;
            }

            product.Name = name;
            product.ObjectPlacement = placement;
            product.Representation = productDefinitionShape;
            if (properties.Count > 0)
            {
                var propertyList = new List<IfcProperty>();
                foreach (var prop in properties)
                {
                    propertyList.Add(CreateProperty(prop.Key, prop.Value));
                }
                var propertySet = CreatePropertySet(propertyList);
                CreateRelDefinesByProperties(product, propertySet);
            }
            site.AddElement(product);
            return product;
        }

        internal static IfcProduct CreateProduct(string name, string type, IfcRepresentation representation, IfcSite site, IfcBuildingStorey buildingStorey, Dictionary<string, string> properties)
        {
            IfcProduct product;
            var placement = CreateLocalPlacement(Representation.RootMatrix.Transform(new XbimPoint3D(0.0, 0.0, 0.0)), Representation.RootMatrix.Transform(new XbimVector3D(0.0, 0.0, 1.0)),
                Representation.RootMatrix.Transform(new XbimVector3D(1.0, 0.0, 0.0)), site.ObjectPlacement);
            var productDefinitionShape = CreateProductDefinitionShape(representation);
            type = type.Split(' ').First();
            switch (type)
            {
                case "EQUI":
                    product = _modelStore.Instances.New<IfcEquipmentElement>();
                    break;
                case "HVAC":
                    product = _modelStore.Instances.New<IfcFlowSegment>();
                    break;
                case "PIPE":
                    product = _modelStore.Instances.New<IfcFlowSegment>();
                    break;
                case "SCTN":
                    product = _modelStore.Instances.New<IfcBeam>();
                    break;
                case "GWALL":
                    product = _modelStore.Instances.New<IfcWall>();
                    break;
                case "FLOOR":
                    product = _modelStore.Instances.New<IfcPlate>();
                    break;
                case "PANE":
                    product = _modelStore.Instances.New<IfcPlate>();
                    break;
                case "CWBRAN":
                    product = _modelStore.Instances.New<IfcElectricalElement>();
                    break;
                case "CABLE":
                    product = _modelStore.Instances.New<IfcElectricalElement>();
                    break;
                default:
                    product = _modelStore.Instances.New<IfcBuildingElementProxy>();
                    break;
            }

            product.Name = name;
            product.ObjectPlacement = placement;
            product.Representation = productDefinitionShape;
            if (properties.Count > 0)
            {
                var propertyList = new List<IfcProperty>();
                foreach (var prop in properties)
                {
                    propertyList.Add(CreateProperty(prop.Key, prop.Value));
                }
                var propertySet = CreatePropertySet(propertyList);
                CreateRelDefinesByProperties(product, propertySet);
            }

            var relContainedInSpatialStructure = _modelStore.Instances.New<IfcRelContainedInSpatialStructure>();
            relContainedInSpatialStructure.RelatingStructure = buildingStorey;
            relContainedInSpatialStructure.RelatedElements.Add(product);

            return product;
        }

        internal static IfcPropertySingleValue CreateProperty(string name, object value)
        {
            var hash =
                 GetEntityHash(new object[]
                 {
                    typeof (IfcPropertySingleValue),
                    name.GetType(), name,
                    value.GetType(),value
                 });
            if (Entities.ContainsKey(hash)) return (IfcPropertySingleValue)Entities[hash];

            var propertySingleValue = _modelStore.Instances.New<IfcPropertySingleValue>();
            propertySingleValue.Name = name;
            if (value is int)
            {
                propertySingleValue.NominalValue = new IfcInteger(value.ToString());
            }
            else
            {
                propertySingleValue.NominalValue = new IfcIdentifier(value.ToString());
            }

            Entities.Add(hash, propertySingleValue);
            return propertySingleValue;
        }

        internal static IfcPropertySet CreatePropertySet(List<IfcProperty> properties)
        {
            var propertySet = _modelStore.Instances.New<IfcPropertySet>();
            propertySet.Name = "Aveva";
            propertySet.Description = "Атрибуты Aveva";
            propertySet.HasProperties.AddRange(properties);
            return propertySet;
        }

        internal static void CreateRelDefinesByProperties(IfcProduct product,
            IfcPropertySet propertySet)
        {
            var relDefinesByProperties = _modelStore.Instances.New<IfcRelDefinesByProperties>();
            relDefinesByProperties.RelatingPropertyDefinition = propertySet;
            relDefinesByProperties.RelatedObjects.Add(product);
        }

        internal static IfcProductDefinitionShape CreateProductDefinitionShape(IfcRepresentation representation)
        {
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcProductDefinitionShape),
                    representation.GetType(), representation.EntityLabel
                });
            if (Entities.ContainsKey(hash)) return (IfcProductDefinitionShape)Entities[hash];

            var productDefinitionShape = _modelStore.Instances.New<IfcProductDefinitionShape>();
            productDefinitionShape.Representations.Add(representation);
            Entities.Add(hash, productDefinitionShape);
            return productDefinitionShape;
        }

        internal static IfcPresentationLayerAssignment CreatePresentationLayerAssignment(string layerName, IfcRepresentation representation)
        {
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcPresentationLayerAssignment),
                    layerName.GetType(), layerName,
                    representation.GetType(), representation.EntityLabel
                });

            IfcPresentationLayerAssignment presentationLayerAssignment;
            if (Entities.ContainsKey(hash))
            {
                presentationLayerAssignment = (IfcPresentationLayerAssignment)Entities[hash];
            }
            else
            {
                presentationLayerAssignment = _modelStore.Instances.New<IfcPresentationLayerAssignment>();
                presentationLayerAssignment.Name = layerName;
                Entities.Add(hash, presentationLayerAssignment);
            }
            if (!presentationLayerAssignment.AssignedItems.Contains(representation))
                presentationLayerAssignment.AssignedItems.Add(representation);
            return presentationLayerAssignment;
        }

        internal static IfcRevolvedAreaSolid CreateRevolvedAreaSolid(IfcProfileDef profileDef,
            IfcAxis2Placement3D axis2Placement3D, IfcAxis1Placement axis1Placement, double angle)
        {
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcRevolvedAreaSolid), profileDef.GetType(), profileDef.EntityLabel,
                    axis2Placement3D.GetType(), axis2Placement3D.EntityLabel,
                    axis1Placement.GetType(), axis1Placement.EntityLabel,
                    angle.GetType(), angle
                });
            if (Entities.ContainsKey(hash)) return (IfcRevolvedAreaSolid)Entities[hash];
            var revolvedAreaSolid = _modelStore.Instances.New<IfcRevolvedAreaSolid>();
            revolvedAreaSolid.Angle = new IfcPlaneAngleMeasure(Math.Abs(angle));
            revolvedAreaSolid.Axis = axis1Placement;
            revolvedAreaSolid.SweptArea = profileDef;
            revolvedAreaSolid.Position = axis2Placement3D;
            Entities.Add(hash, revolvedAreaSolid);
            return revolvedAreaSolid;
        }

        internal static IfcAxis1Placement CreateAxis1Placement(IfcCartesianPoint location, IfcDirection direction)
        {
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcAxis1Placement),
                    location.GetType(), location.EntityLabel,
                    direction.GetType(), direction.EntityLabel
                });
            if (Entities.ContainsKey(hash)) return (IfcAxis1Placement)Entities[hash];

            var axis1Placement = _modelStore.Instances.New<IfcAxis1Placement>();
            axis1Placement.Location = location;
            axis1Placement.Axis = direction;
            Entities.Add(hash, axis1Placement);
            return axis1Placement;
        }

        internal static IfcCartesianPoint CreateCartesianPoint(XbimPoint3D point)
        {
            point = point.Round();
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcCartesianPoint),
                    point.GetType(), point.X, point.Y, point.Z
                });
            if (Entities.ContainsKey(hash)) return (IfcCartesianPoint)Entities[hash];

            var cartesianPoint = _modelStore.Instances.New<IfcCartesianPoint>();
            cartesianPoint.X = point.X;
            cartesianPoint.Y = point.Y;
            if (point.Z != double.MinValue)
                cartesianPoint.Z = point.Z;
            Entities.Add(hash, cartesianPoint);
            return cartesianPoint;
        }

        internal static IfcCartesianPoint CreateCartesianPoint2(XbimPoint3D point)
        {
            point = point.Round();
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcCartesianPoint),
                    point.GetType(), point.X, point.Y
                });

            if (Entities.ContainsKey(hash))
                return (IfcCartesianPoint)Entities[hash];

            var cartesianPoint = _modelStore.Instances.New<IfcCartesianPoint>();
            cartesianPoint.SetXY(point.X, point.Y);

            Entities.Add(hash, cartesianPoint);
            return cartesianPoint;
        }

        internal static IfcDirection CreateDirection(XbimVector3D vector)
        {
            vector = vector.Round();
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcDirection),
                    vector.GetType(), vector.X, vector.Y, vector.Z
                });
            if (Entities.ContainsKey(hash)) return (IfcDirection)Entities[hash];

            var direction = _modelStore.Instances.New<IfcDirection>();
            direction.X = vector.X;
            direction.Y = vector.Y;
            if (vector.Z != double.MinValue)
                direction.Z = vector.Z;
            Entities.Add(hash, direction);
            return direction;
        }

        internal static IfcDirection CreateDirection2(XbimVector3D vector)
        {
            vector = vector.Round();
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcDirection),
                    vector.GetType(), vector.X, vector.Y
                });

            if (Entities.ContainsKey(hash))
                return (IfcDirection)Entities[hash];

            var direction = _modelStore.Instances.New<IfcDirection>();
            direction.SetXY(vector.X, vector.Y);

            Entities.Add(hash, direction);
            return direction;
        }

        internal static IfcExtrudedAreaSolid CreateExtrudedAreaSolid(IfcProfileDef profileDef,
            IfcAxis2Placement3D axis2Placement3D, IfcDirection extrudeDirection, double depth)
        {
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcExtrudedAreaSolid),
                    profileDef.GetType(), profileDef.EntityLabel,
                    axis2Placement3D.GetType(), axis2Placement3D.EntityLabel,
                    extrudeDirection.GetType(), extrudeDirection.EntityLabel,
                    depth.GetType(), depth
                });
            if (Entities.ContainsKey(hash)) return (IfcExtrudedAreaSolid)Entities[hash];

            var extrudedAreaSolid = _modelStore.Instances.New<IfcExtrudedAreaSolid>();
            extrudedAreaSolid.Depth = depth;
            extrudedAreaSolid.ExtrudedDirection = extrudeDirection;
            extrudedAreaSolid.SweptArea = profileDef;
            extrudedAreaSolid.Position = axis2Placement3D;
            Entities.Add(hash, extrudedAreaSolid);
            return extrudedAreaSolid;
        }

        internal static IfcCircleProfileDef CreateCircleProfileDef(double radius, double axisOffset = 0.0)
        {
            radius = radius.Round();
            axisOffset = axisOffset.Round();

            var hash = GetEntityHash(new object[]
            {
                typeof (IfcCircleProfileDef),
                radius.GetType(), radius,
                axisOffset.GetType(), axisOffset
            });
            if (Entities.ContainsKey(hash)) return (IfcCircleProfileDef)Entities[hash];

            var axis2Placement2D = CreateAxis2Placement2D(new XbimPoint3D(0.0, axisOffset, double.MinValue));

            var circleProfileDef = _modelStore.Instances.New<IfcCircleProfileDef>();
            circleProfileDef.ProfileType = IfcProfileTypeEnum.AREA;
            circleProfileDef.Position = axis2Placement2D;
            circleProfileDef.Radius = radius;
            Entities.Add(hash, circleProfileDef);
            return circleProfileDef;
        }

        internal static IfcEllipse CreateEllipse(IfcAxis2Placement2D axis2Placement2D, double r1, double r2)
        {
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcEllipse),
                    axis2Placement2D.GetType(), axis2Placement2D.EntityLabel,
                    r1.GetType(), r1,
                    r2.GetType(), r2
                });
            if (Entities.ContainsKey(hash)) return (IfcEllipse)Entities[hash];
            var ellipse = _modelStore.Instances.New<IfcEllipse>();
            ellipse.Position = axis2Placement2D;
            ellipse.SemiAxis1 = new IfcPositiveLengthMeasure(r1);
            ellipse.SemiAxis2 = new IfcPositiveLengthMeasure(r2);
            Entities.Add(hash, ellipse);
            return ellipse;
        }

        internal static IfcArbitraryClosedProfileDef CreateArbitraryClosedProfileDef(IfcCurve curve,
            IfcProfileTypeEnum profileType)
        {
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcArbitraryClosedProfileDef),
                    curve.GetType(), curve.EntityLabel,
                    profileType.GetType(), profileType
                });

            if (Entities.ContainsKey(hash))
                return (IfcArbitraryClosedProfileDef)Entities[hash];

            var arbitraryClosedProfileDef = _modelStore.Instances.New<IfcArbitraryClosedProfileDef>();
            arbitraryClosedProfileDef.OuterCurve = curve;
            arbitraryClosedProfileDef.ProfileType = profileType;

            Entities.Add(hash, arbitraryClosedProfileDef);

            return arbitraryClosedProfileDef;
        }

        internal static IfcArbitraryProfileDefWithVoids CreateArbitraryProfileDefWithVoids(IfcPolyline outerCurve, List<IfcPolyline> innerCurves, IfcProfileTypeEnum profileType)
        {
            var hList = new List<object> { typeof(IfcArbitraryProfileDefWithVoids), outerCurve.GetType(), outerCurve.EntityLabel };
            foreach (var polyLine in innerCurves)
            {
                hList.Add(polyLine.GetType());
                hList.Add(polyLine.EntityLabel);
            }

            var hash = GetEntityHash(hList.ToArray());

            if (Entities.ContainsKey(hash))
                return (IfcArbitraryProfileDefWithVoids)Entities[hash];

            var arbitraryProfileDefWithVoids = _modelStore.Instances.New<IfcArbitraryProfileDefWithVoids>();
            arbitraryProfileDefWithVoids.OuterCurve = outerCurve;
            arbitraryProfileDefWithVoids.InnerCurves.AddRange(innerCurves);
            arbitraryProfileDefWithVoids.ProfileType = profileType;

            Entities.Add(hash, arbitraryProfileDefWithVoids);

            return arbitraryProfileDefWithVoids;
        }

        internal static IfcCompositeCurve CreateCompositeCurve(List<IfcCompositeCurveSegment> segments,
            bool selfIntersect)
        {
            var hList = new List<object> { typeof(IfcCompositeCurve) };
            foreach (var segment in segments)
            {
                hList.Add(segment.GetType());
                hList.Add(segment.EntityLabel);
            }
            hList.Add(selfIntersect.GetType());
            hList.Add(selfIntersect);
            var hash = GetEntityHash(hList.ToArray());
            if (Entities.ContainsKey(hash)) return (IfcCompositeCurve)Entities[hash];
            var compositeCurve = _modelStore.Instances.New<IfcCompositeCurve>();
            foreach (var segment in segments)
                compositeCurve.Segments.Add(segment);
            compositeCurve.SelfIntersect = selfIntersect;
            Entities.Add(hash, compositeCurve);
            return compositeCurve;
        }

        internal static IfcCompositeCurveSegment CreateCompositeCurveSegment(IfcCurve curve,
            IfcTransitionCode transitionCode, bool sameSense)
        {
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcCompositeCurveSegment),
                    curve.GetType(), curve.EntityLabel,
                    transitionCode.GetType(), transitionCode,
                    sameSense.GetType(), sameSense
                });
            if (Entities.ContainsKey(hash)) return (IfcCompositeCurveSegment)Entities[hash];
            var compositeCurveSegment = _modelStore.Instances.New<IfcCompositeCurveSegment>();
            compositeCurveSegment.ParentCurve = curve;
            compositeCurveSegment.Transition = transitionCode;
            compositeCurveSegment.SameSense = sameSense;
            Entities.Add(hash, compositeCurveSegment);
            return compositeCurveSegment;
        }

        internal static IfcPolyline CreatePolyLine(List<IfcCartesianPoint> points)
        {
            var hList = new List<object> { typeof(IfcPolyline) };
            foreach (var p in points)
            {
                hList.Add(p.GetType());
                hList.Add(p.EntityLabel);
            }

            var hash = GetEntityHash(hList.ToArray());

            if (Entities.ContainsKey(hash))
                return (IfcPolyline)Entities[hash];

            var polyLine = _modelStore.Instances.New<IfcPolyline>();
            foreach (var p in points)
            {
                polyLine.Points.Add(p);
            }

            Entities.Add(hash, polyLine);
            return polyLine;
        }

        internal static IfcTrimmedCurve CreateTrimmedCurve(IfcCurve curve, IfcTrimmingPreference trimmingPreference,
            IfcCartesianPoint p1, IfcCartesianPoint p2, bool senseAgreement)
        {
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcTrimmedCurve),
                    curve.GetType(), curve.EntityLabel,
                    trimmingPreference.GetType(), trimmingPreference,
                    p1.GetType(), p1.EntityLabel,
                    p2.GetType(), p2.EntityLabel,
                    senseAgreement.GetType(), senseAgreement
                });

            if (Entities.ContainsKey(hash)) return (IfcTrimmedCurve)Entities[hash];
            var trimmedCurve = _modelStore.Instances.New<IfcTrimmedCurve>();
            trimmedCurve.BasisCurve = curve;
            trimmedCurve.MasterRepresentation = trimmingPreference;
            trimmedCurve.Trim1.Add(p1);
            trimmedCurve.Trim2.Add(p2);
            trimmedCurve.SenseAgreement = senseAgreement;
            Entities.Add(hash, trimmedCurve);
            return trimmedCurve;
        }

        internal static IfcCircle CreateCircle(double sphereRadius, IfcAxis2Placement2D axis2Placement2D)
        {
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcCircle),
                    sphereRadius.GetType(), sphereRadius,
                    axis2Placement2D.GetType(), axis2Placement2D.EntityLabel
                });
            if (Entities.ContainsKey(hash)) return (IfcCircle)Entities[hash];
            var circle = _modelStore.Instances.New<IfcCircle>();
            circle.Radius = sphereRadius;
            circle.Position = axis2Placement2D;
            Entities.Add(hash, circle);
            return circle;
        }

        internal static IfcColourRgb CreateColourRgb(double[] rgb)
        {
            var hash = GetEntityHash(new object[]
            {
                typeof (IfcColourRgb),
                rgb.GetType(), rgb[0], rgb[1], rgb[2]
            });
            if (Entities.ContainsKey(hash)) return (IfcColourRgb)Entities[hash];

            var diffColourRgb = _modelStore.Instances.New<IfcColourRgb>();
            diffColourRgb.Red = rgb[0];
            diffColourRgb.Green = rgb[1];
            diffColourRgb.Blue = rgb[2];
            Entities.Add(hash, diffColourRgb);
            return diffColourRgb;
        }

        internal static IfcSurfaceStyleRendering CreateSurfaceStyleRendering(IfcColourRgb diffColourRgb,
            double translucency)
        {
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcSurfaceStyleRendering),
                    diffColourRgb.GetType(), diffColourRgb.EntityLabel,
                    translucency.GetType(), translucency
                });
            if (Entities.ContainsKey(hash)) return (IfcSurfaceStyleRendering)Entities[hash];

            var surfaceStyleRendering = _modelStore.Instances.New<IfcSurfaceStyleRendering>();
            surfaceStyleRendering.Transparency = translucency;
            surfaceStyleRendering.SpecularColour = new IfcNormalisedRatioMeasure(0.3);
            surfaceStyleRendering.SpecularHighlight = new IfcSpecularExponent(6.0);
            surfaceStyleRendering.SurfaceColour = diffColourRgb;
            surfaceStyleRendering.ReflectanceMethod = IfcReflectanceMethodEnum.NOTDEFINED;

            Entities.Add(hash, surfaceStyleRendering);
            return surfaceStyleRendering;
        }

        internal static IfcSurfaceStyle CreateSurfaceStyle(IfcSurfaceStyleRendering surfaceStyleRendering,
            int colorIndex)
        {
            var hash = GetEntityHash(new object[]
            {
                typeof (IfcSurfaceStyle),
                surfaceStyleRendering.GetType(), surfaceStyleRendering.EntityLabel,
                colorIndex.GetType(), colorIndex
            });
            if (Entities.ContainsKey(hash)) return (IfcSurfaceStyle)Entities[hash];

            var surfaceStyle = _modelStore.Instances.New<IfcSurfaceStyle>();
            surfaceStyle.Name = string.Format("material {0}", colorIndex);
            surfaceStyle.Side = IfcSurfaceSide.BOTH;
            surfaceStyle.Styles.Add(surfaceStyleRendering);
            Entities.Add(hash, surfaceStyle);
            return surfaceStyle;
        }

        internal static IfcPresentationStyleAssignment CreatePresentationStyleAssignment(IfcPresentationStyle presentationStyle)
        {
            var hash = GetEntityHash(new object[]
            {
                typeof (IfcPresentationStyleAssignment),
                presentationStyle.GetType(), presentationStyle.EntityLabel
            });

            if (Entities.ContainsKey(hash))
                return (IfcPresentationStyleAssignment)Entities[hash];

            var presentationStyleAssignment = _modelStore.Instances.New<IfcPresentationStyleAssignment>();
            presentationStyleAssignment.Styles.Add(presentationStyle as IfcPresentationStyleSelect);

            Entities.Add(hash, presentationStyleAssignment);

            return presentationStyleAssignment;
        }

        internal static IfcStyledItem CreateStyledItem(IfcRepresentationItem representationItem,
            IfcPresentationStyleAssignment presentationStyleAssignment)
        {
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcStyledItem),
                    representationItem.GetType(), representationItem.EntityLabel,
                    presentationStyleAssignment.GetHashCode(), presentationStyleAssignment.EntityLabel
                });
            if (Entities.ContainsKey(hash)) return (IfcStyledItem)Entities[hash];
            var styledItem = _modelStore.Instances.New<IfcStyledItem>();
            styledItem.Item = representationItem;
            styledItem.Styles.Add(presentationStyleAssignment);
            Entities.Add(hash, styledItem);
            return styledItem;
        }

        internal static IfcBooleanResult CreateBooleanResult(IfcBooleanOperand first, IfcBooleanOperand second,
            IfcBooleanOperator booleanOperator)
        {
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcBooleanResult),
                    first.GetType(), first.EntityLabel,
                    second.GetType(), second.EntityLabel,
                    booleanOperator.GetType(), booleanOperator
                });
            if (Entities.ContainsKey(hash)) return (IfcBooleanResult)Entities[hash];
            var booleanResult = _modelStore.Instances.New<IfcBooleanResult>();
            booleanResult.Operator = booleanOperator;
            booleanResult.FirstOperand = first;
            booleanResult.SecondOperand = second;
            Entities.Add(hash, booleanResult);
            return booleanResult;
        }

        internal static IfcBooleanClippingResult CreateBooleanClippingResult(IfcBooleanOperand first,
            IfcBooleanOperand second, IfcBooleanOperator booleanOperator)
        {
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcBooleanClippingResult),
                    first.GetType(), first.EntityLabel,
                    second.GetType(), second.EntityLabel,
                    booleanOperator.GetType(), booleanOperator
                });
            if (Entities.ContainsKey(hash)) return (IfcBooleanClippingResult)Entities[hash];

            var booleanClippingResult = _modelStore.Instances.New<IfcBooleanClippingResult>();
            booleanClippingResult.FirstOperand = first;
            booleanClippingResult.SecondOperand = second;
            booleanClippingResult.Operator = booleanOperator;
            Entities.Add(hash, booleanClippingResult);
            return booleanClippingResult;
        }

        internal static IfcHalfSpaceSolid CreateHalfSpaceSolid(IfcPlane plane, bool agreementFlag)
        {
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcHalfSpaceSolid),
                    plane.GetType(), plane.EntityLabel,
                    agreementFlag.GetType(), agreementFlag
                });
            if (Entities.ContainsKey(hash)) return (IfcHalfSpaceSolid)Entities[hash];

            var halfSpaceSolid = _modelStore.Instances.New<IfcHalfSpaceSolid>();
            halfSpaceSolid.BaseSurface = plane;
            halfSpaceSolid.AgreementFlag = agreementFlag;
            Entities.Add(hash, halfSpaceSolid);
            return halfSpaceSolid;
        }

        internal static IfcPlane CreatePlane(IfcAxis2Placement3D axis2Placement3D)
        {
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcPlane),
                    axis2Placement3D.GetType(), axis2Placement3D.EntityLabel
                });
            if (Entities.ContainsKey(hash)) return (IfcPlane)Entities[hash];

            var planeBottom = _modelStore.Instances.New<IfcPlane>();
            planeBottom.Position = axis2Placement3D;
            Entities.Add(hash, planeBottom);
            return planeBottom;
        }

        internal static IfcAxis2Placement3D CreateAxis2Placement3D(XbimPoint3D point, XbimVector3D axis,
            XbimVector3D refDirection, XbimMatrix3D m)
        {
            if (!m.IsIdentity)
            {
                point = m.Transform(point);
                axis = m.Transform(axis);
                refDirection = m.Transform(refDirection);
            }

            point = point.Round();
            axis = axis.Round();
            refDirection = refDirection.Round();

            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcAxis2Placement3D),
                    point.GetType(), point.X, point.Y, point.Z,
                    axis.GetType(), axis.X, axis.Y, axis.Z,
                    refDirection.GetType(), refDirection.X, refDirection.Y, refDirection.Z
                });

            if (Entities.ContainsKey(hash))
                return (IfcAxis2Placement3D)Entities[hash];

            var axis2Placement3D = _modelStore.Instances.New<IfcAxis2Placement3D>();
            axis2Placement3D.Location = CreateCartesianPoint(point);
            axis2Placement3D.Axis = CreateDirection(axis);
            axis2Placement3D.RefDirection = CreateDirection(refDirection);

            Entities.Add(hash, axis2Placement3D);
            return axis2Placement3D;
        }

        internal static IfcAxis2Placement2D CreateAxis2Placement2D(XbimPoint3D point)
        {
            point = point.Round();
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcAxis2Placement2D),
                    point.GetType(), point.X, point.Y, point.Z
                });

            if (Entities.ContainsKey(hash))
                return (IfcAxis2Placement2D)Entities[hash];

            var axis2Placement2D = _modelStore.Instances.New<IfcAxis2Placement2D>();
            axis2Placement2D.Location = CreateCartesianPoint2(point);
            axis2Placement2D.RefDirection = CreateDirection2(new XbimVector3D(1.0, 0.0, 0.0));

            Entities.Add(hash, axis2Placement2D);
            return axis2Placement2D;
        }

        internal static IfcLocalPlacement CreateLocalPlacement(XbimPoint3D point, XbimVector3D axis,
            XbimVector3D refDirection, IfcObjectPlacement placementRelTo)
        {
            var relativePlacement = CreateAxis2Placement3D(point, axis, refDirection, XbimMatrix3D.Identity);
            var placement = _modelStore.Instances.New<IfcLocalPlacement>();
            placement.RelativePlacement = relativePlacement;
            placement.PlacementRelTo = placementRelTo;
            return placement;
        }

        internal static IfcFacetedBrep CreateFacetedBrep(IfcClosedShell closedShell)
        {
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcFacetedBrep),
                    closedShell.GetType(), closedShell.EntityLabel
                });
            if (Entities.ContainsKey(hash)) return (IfcFacetedBrep)Entities[hash];

            var facetedBrep = _modelStore.Instances.New<IfcFacetedBrep>();
            facetedBrep.Outer = closedShell;
            Entities.Add(hash, facetedBrep);
            return facetedBrep;
        }

        internal static IfcClosedShell CreateClosedShell(List<List<XbimPoint3D>> input)
        {
            var faces = input.Select(inputFace => inputFace.Select(p => p.Round()).ToList()).ToList();

            var hList = new List<object> { typeof(IfcClosedShell) };
            foreach (var f in faces)
            {
                hList.Add(f.GetType());
                foreach (var p in f)
                {
                    hList.Add(p.GetType());
                    hList.Add(p.X);
                    hList.Add(p.Y);
                    hList.Add(p.Z);
                }
            }
            var hash = GetEntityHash(hList.ToArray());
            if (Entities.ContainsKey(hash)) return (IfcClosedShell)Entities[hash];

            var closedShell = _modelStore.Instances.New<IfcClosedShell>();
            foreach (var f in faces)
            {
                var polyLoop = CreatePolyLoop(f);
                var faceOuterBound = CreateFaceOuterBound(polyLoop);
                var face = CreateFace(faceOuterBound);
                closedShell.CfsFaces.Add(face);
            }
            Entities.Add(hash, closedShell);
            return closedShell;
        }

        internal static IfcFace CreateFace(IfcFaceOuterBound faceOuterBound)
        {
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcFace),
                    faceOuterBound.GetType(), faceOuterBound.EntityLabel
                });

            if (Entities.ContainsKey(hash))
                return (IfcFace)Entities[hash];

            var face = _modelStore.Instances.New<IfcFace>();
            face.Bounds.Add(faceOuterBound);

            Entities.Add(hash, face);

            return face;
        }

        internal static IfcFaceOuterBound CreateFaceOuterBound(IfcPolyLoop polyLoop)
        {
            var hash =
                GetEntityHash(new object[]
                {
                    typeof (IfcFaceOuterBound),
                    polyLoop.GetType(), polyLoop.EntityLabel
                });

            if (Entities.ContainsKey(hash))
                return (IfcFaceOuterBound)Entities[hash];

            var faceOuterBound = _modelStore.Instances.New<IfcFaceOuterBound>();
            faceOuterBound.Orientation = true;
            faceOuterBound.Bound = polyLoop;

            Entities.Add(hash, faceOuterBound);

            return faceOuterBound;
        }

        private static IfcPolyLoop CreatePolyLoop(List<XbimPoint3D> input)
        {
            var hList = new List<object> { typeof(IfcPolyLoop) };
            var points = input.Select(p => p.Round()).ToList();
            foreach (var p in points)
            {
                hList.Add(p.GetType());
                hList.Add(p.X);
                hList.Add(p.Y);
                hList.Add(p.Z);
            }
            var hash = GetEntityHash(hList.ToArray());
            if (Entities.ContainsKey(hash)) return (IfcPolyLoop)Entities[hash];
            var polyLoop = _modelStore.Instances.New<IfcPolyLoop>();
            foreach (var point in points)
            {
                polyLoop.Polygon.Add(CreateCartesianPoint(point));
            }
            Entities.Add(hash, polyLoop);
            return polyLoop;
        }

        internal static IfcShapeRepresentation CreateShapeRepresentation(List<IfcRepresentationItem> items,
            string identifier, string type)
        {
            var hList = new List<object>
            {
                typeof (IfcShapeRepresentation),
                identifier.GetType(),
                identifier,
                type.GetType(),
                type,
                _modelContext.GetType(),
                _modelContext.EntityLabel
            };

            foreach (var item in items)
            {
                hList.Add(item.GetType());
                hList.Add(item.EntityLabel);
            }

            var hash = GetEntityHash(hList.ToArray());
            if (Entities.ContainsKey(hash)) return (IfcShapeRepresentation)Entities[hash];

            var shapeRepresentation = _modelStore.Instances.New<IfcShapeRepresentation>();
            shapeRepresentation.ContextOfItems = _modelContext;
            shapeRepresentation.RepresentationIdentifier = identifier;
            shapeRepresentation.RepresentationType = type;
            shapeRepresentation.Items.AddRange(items);
            Entities.Add(hash, shapeRepresentation);
            return shapeRepresentation;
        }

        internal static IfcRectangleProfileDef CreateRectangleProfileDef(double xlen, double ylen,
            double axisOffset = 0.0)
        {
            xlen = xlen.Round();
            ylen = ylen.Round();
            axisOffset = axisOffset.Round();
            var hash = GetEntityHash(new object[]
            {
                typeof (IfcRectangleProfileDef),
                xlen.GetType(), xlen,
                ylen.GetType(), ylen,
                axisOffset.GetType(), axisOffset
            });
            if (Entities.ContainsKey(hash)) return (IfcRectangleProfileDef)Entities[hash];

            var axis2Placement2D = CreateAxis2Placement2D(new XbimPoint3D(0.0, axisOffset, double.MinValue));

            var rectangleProfileDef = _modelStore.Instances.New<IfcRectangleProfileDef>();
            rectangleProfileDef.ProfileType = IfcProfileTypeEnum.AREA;
            rectangleProfileDef.XDim = xlen;
            rectangleProfileDef.YDim = ylen;
            rectangleProfileDef.Position = axis2Placement2D;
            Entities.Add(hash, rectangleProfileDef);
            return rectangleProfileDef;
        }

        //internal static IfcRepresentationMap CreateRepresentationMap(IfcAxis2Placement3D axis2Placement3D,
        //    IfcShapeRepresentation shapeRepresentation)
        //{
        //    var hash =
        //        GetEntityHash(new object[]
        //        {
        //            typeof (IfcRepresentationMap),
        //            axis2Placement3D.GetType(), axis2Placement3D.EntityLabel,
        //            shapeRepresentation.GetType(), shapeRepresentation.EntityLabel
        //        });
        //    if (Entities.ContainsKey(hash)) return (IfcRepresentationMap) Entities[hash];

        //    var representationMap = _modelStore.Instances.New<IfcRepresentationMap>();
        //    representationMap.MappingOrigin = axis2Placement3D;
        //    representationMap.MappedRepresentation = shapeRepresentation;
        //    Entities.Add(hash, representationMap);
        //    return representationMap;
        //}

        //internal static IfcMappedItem CreateMappedItem(IfcRepresentationMap representationMap,
        //    IfcCartesianTransformationOperator3D cartesianTransformationOperator3D)
        //{
        //    var hash =
        //        GetEntityHash(new object[]
        //        {
        //            typeof (IfcRepresentationMap),
        //            representationMap.GetType(), representationMap.EntityLabel,
        //            cartesianTransformationOperator3D.GetType(), cartesianTransformationOperator3D.EntityLabel
        //        });
        //    if (Entities.ContainsKey(hash)) return (IfcMappedItem) Entities[hash];

        //    var mappedItem = _modelStore.Instances.New<IfcMappedItem>();
        //    mappedItem.MappingSource = representationMap;
        //    mappedItem.MappingTarget = cartesianTransformationOperator3D;
        //    Entities.Add(hash, mappedItem);
        //    return mappedItem;
        //}

        internal static string GetEntityHash(object[] data)
        {
            MD5 md5 = MD5.Create();
            var inputBytes = new List<byte>();
            for (var i = 1; i < data.Length; i++)
            {
                var d = data[i];
                inputBytes.AddRange(Encoding.ASCII.GetBytes(d + "#"));
            }

            var hash = md5.ComputeHash(inputBytes.ToArray());
            var sb = new StringBuilder();
            sb.Append(((Type)data[0]).Name);
            foreach (var b in hash)
            {
                sb.Append(b.ToString("X2"));
            }

            return sb.ToString();
        }
    }
}