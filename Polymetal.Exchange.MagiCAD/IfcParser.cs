using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Polymetal.Exchange.Utilities;
using Tekla.Structures.Geometry3d;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Ifc2x3.Extensions;
using Xbim.Ifc2x3.GeometricModelResource;
using Xbim.Ifc2x3.GeometryResource;
using Xbim.Ifc2x3.IO;
using Xbim.Ifc2x3.Kernel;
using Xbim.Ifc2x3.ProductExtension;
using Xbim.Ifc2x3.ProfileResource;
using Xbim.Ifc2x3.PropertyResource;
using Xbim.Ifc2x3.RepresentationResource;
using Xbim.Ifc2x3.SharedBldgServiceElements;
using Xbim.Ifc2x3.TopologyResource;

namespace Polymetal.Exchange.MagiCAD
{
    class IfcParser
    {
        public static readonly string DecimalSeparator = NumberFormatInfo.CurrentInfo.CurrencyDecimalSeparator;
        private static Dictionary<string, MagicSystem> _systemDict = new Dictionary<string, MagicSystem>();

        internal static Dictionary<string[], MagicItem> RunParse(string ifcPath)
        {
            var magicItems = new Dictionary<string[], MagicItem>();
            if (string.IsNullOrEmpty(ifcPath)) return magicItems;
            var ifcFile = new FileInfo(ifcPath);
            if (!ifcFile.Exists) return magicItems;
            var xBimPath = ifcFile.FullName.Replace(ifcFile.Extension, ".xBIM");
            Console.WriteLine("{0}: Обрабокта файла {1}",DateTime.Now.ToLongTimeString(), ifcPath);
            
            using (var model = new XbimModel())
            {
                if (model.CreateFrom(ifcFile.FullName, xBimPath))
                {
                    model.Open(xBimPath);
                    magicItems = AnalyzeModel(model.IfcProducts);
                    model.Close();
                }
            }
            Console.WriteLine("{0}: IFC файл обработан {1}", DateTime.Now.ToLongTimeString(), ifcPath);
            if (File.Exists(xBimPath))
                File.Delete(xBimPath);

            return magicItems;
        }

        private static Dictionary<string[], MagicItem> AnalyzeModel(IEnumerable<IPersistEntity> entities)
        {
            var result = new Dictionary<string[], MagicItem>();
            foreach (IfcProduct entity in entities)
            {
                //if (entity is IfcCovering) continue;
                var repr = entity.Representation;
                if (repr == null) continue;
                var entityInfo = new MagicItem
                {
                    Description = entity.Description,
                    Coverings = new List<string>(),
                    Properties = GetEntityProperties(entity)
                };

                var ignore = false;
                foreach (var prop in entityInfo.Properties)
                {
                    if (!prop.StartsWith("Pdms_UserVar4")) continue;
                    ignore = prop.Split('#').Last().Trim().Equals("ignore", StringComparison.OrdinalIgnoreCase);
                    break;
                }
                if (ignore) continue;

                if (entity is IfcDistributionFlowElement)
                {
                    var flowSegment = entity as IfcDistributionFlowElement;
                    foreach (IfcRelCoversBldgElements relCoversBldgElement in flowSegment.HasCoverings)
                    {
                        foreach (IfcCovering covering in relCoversBldgElement.RelatedCoverings)
                        {
                            entityInfo.Coverings.Add(covering.GlobalId);
                        }
                    }
                }

                var placement = entity.ObjectPlacement;
                var matrix = placement.ToMatrix3D();
                entityInfo.Matrix = new List<double>
                {
                    matrix.M11,
                    matrix.M12,
                    matrix.M13,
                    matrix.M14,
                    matrix.M21,
                    matrix.M22,
                    matrix.M23,
                    matrix.M24,
                    matrix.M31,
                    matrix.M32,
                    matrix.M33,
                    matrix.M34,
                    matrix.OffsetX,
                    matrix.OffsetY,
                    matrix.OffsetZ,
                    matrix.M44
                };

                entityInfo.Faces = new List<MagicFace>();
                var rep = entity.GetBodyRepresentation();
                foreach (IfcMappedItem shapeItem in rep.Items)
                {
                    var shapeRepresentation = (IfcShapeRepresentation) shapeItem.MappingSource.MappedRepresentation;
                    var scale = 1.0;
                    var scale2 = 1.0;
                    var scale3 = 1.0;
                    if (shapeItem.MappingTarget != null)
                    {
                        if (shapeItem.MappingTarget is IfcCartesianTransformationOperator3DnonUniform)
                        {
                            var ifcTransformation =
                                (IfcCartesianTransformationOperator3DnonUniform) shapeItem.MappingTarget;
                            scale = Double.TryParse(ifcTransformation.Scale.ToString(), out scale) ? scale : 1.0;
                            scale2 = Double.TryParse(ifcTransformation.Scale2.ToString(), out scale2) ? scale2 : 1.0;
                            scale3 = Double.TryParse(ifcTransformation.Scale3.ToString(), out scale3) ? scale3 : 1.0;
                        }
                        else if (shapeItem.MappingTarget is IfcCartesianTransformationOperator3D)
                        {
                            var ifcTransformation = (IfcCartesianTransformationOperator3D) shapeItem.MappingTarget;
                            scale = Double.TryParse(ifcTransformation.Scale.ToString(), out scale) ? scale : 1.0;
                        }
                        else
                        {
                            Console.WriteLine("Unknown ifcTransformation");
                        }
                    }
                    var ifcRepresentationItem = shapeRepresentation.Items.First();
                    if (ifcRepresentationItem is IfcShellBasedSurfaceModel)
                    {
                        var shellBasedSurfaceModel = ifcRepresentationItem as IfcShellBasedSurfaceModel;
                        foreach (IfcOpenShell sbsmBoundary in shellBasedSurfaceModel.SbsmBoundary)
                        {
                            foreach (IfcFace face in sbsmBoundary.CfsFaces)
                            {
                                foreach (IfcFaceBound faceBound in face.Bounds)
                                {
                                    var faceInfo = new MagicFace {Points = new List<double[]>()};

                                    //foreach (IVertex3D vertex in faceBound.ClockWiseVertices)
                                    //{
                                    //    var xbimPoint = new XbimPoint3D(vertex.X * scale, vertex.Y * scale, vertex.Z * scale);

                                    //    faceInfo.Points.Add(new[] { xbimPoint.X, xbimPoint.Y, xbimPoint.Z });
                                    //}
                                    var loop = faceBound.Bound as IfcPolyLoop;
                                    foreach (var vertex in loop.Polygon)
                                    {
                                        var xbimPoint = new XbimPoint3D(vertex.X*scale, vertex.Y*scale, vertex.Z*scale);

                                        faceInfo.Points.Add(new[] {xbimPoint.X, xbimPoint.Y, xbimPoint.Z});
                                    }
                                    var diffFace = false;
                                    if (faceInfo.Points.Count > 3)
                                    {
                                        //var p1 = new XbimPoint3D(faceInfo.Points[0][0], faceInfo.Points[0][1], faceInfo.Points[0][2]);
                                        //var tm = new XbimMatrix3D() { OffsetX = p1.X, OffsetY = p1.Y, OffsetZ = p1.Z };
                                        //var p2 = tm.Transform(new XbimPoint3D(faceInfo.Points[1][0], faceInfo.Points[1][1], faceInfo.Points[1][2]));
                                        //var p3 = tm.Transform(new XbimPoint3D(faceInfo.Points[2][0], faceInfo.Points[2][1], faceInfo.Points[2][2]));
                                        //var vx = new XbimVector3D(p2.X, p2.Y, p2.Z);
                                        //var vy = new XbimVector3D(p3.X, p3.Y, p3.Z);
                                        //var vz = vx.CrossProduct(vy);
                                        //tm = new XbimMatrix3D
                                        //{
                                        //    M11 = vx.X,
                                        //    M12 = vx.Y,
                                        //    M13 = vx.Z,
                                        //    M21 = vy.X,
                                        //    M22 = vy.Y,
                                        //    M23 = vy.Z,
                                        //    M31 = vz.X,
                                        //    M32 = vz.Y,
                                        //    M33 = vz.Z,
                                        //    OffsetX = p1.X,
                                        //    OffsetY = p1.Y,
                                        //    OffsetZ = p1.Z
                                        //};

                                        //for (var k = 3; k < faceInfo.Points.Count; k++)
                                        //{
                                        //    var p =
                                        //        tm.Transform(new XbimPoint3D(faceInfo.Points[3][0], faceInfo.Points[3][1],
                                        //            faceInfo.Points[3][2]));
                                        //    if (!(Math.Abs(p.Z) > 0.01)) continue;

                                        //    diffFace = true;
                                        //    break;
                                        //}
                                        var p1 = new Point(faceInfo.Points[0][0], faceInfo.Points[0][1],
                                            faceInfo.Points[0][2]);
                                        var cs = new CoordinateSystem {Origin = p1};
                                        var tm = MatrixFactory.ToCoordinateSystem(cs);
                                        var p2 =
                                            tm.Transform(new Point(faceInfo.Points[1][0], faceInfo.Points[1][1],
                                                faceInfo.Points[1][2]));
                                        var p3 =
                                            tm.Transform(new Point(faceInfo.Points[2][0], faceInfo.Points[2][1],
                                                faceInfo.Points[2][2]));
                                        var vx = new Vector(p2);
                                        var vy = new Vector(p3);
                                        cs = new CoordinateSystem(p1, vx, vy);
                                        tm = MatrixFactory.ToCoordinateSystem(cs);

                                        for (var k = 3; k < faceInfo.Points.Count; k++)
                                        {
                                            var p =
                                                tm.Transform(new Point(faceInfo.Points[3][0], faceInfo.Points[3][1],
                                                    faceInfo.Points[3][2]));
                                            if (Math.Abs(p.Z) > 0.01)
                                            {
                                                diffFace = true;
                                                break;
                                            }
                                        }
                                    }
                                    if (diffFace)
                                    {
                                        var p1 = faceInfo.Points[0];
                                        for (var k = 1; k < faceInfo.Points.Count - 1; k++)
                                        {
                                            var p2 = faceInfo.Points[k];
                                            var p3 = faceInfo.Points[k + 1];
                                            entityInfo.Faces.Add(new MagicFace()
                                            {
                                                Points = new List<double[]>() {p1, p2, p3}
                                            });
                                        }
                                    }
                                    else
                                    {
                                        entityInfo.Faces.Add(faceInfo);
                                    }
                                }
                            }
                        }
                    }
                    else if (ifcRepresentationItem is IfcExtrudedAreaSolid)
                    {
                        entityInfo.Solid = new MagicSolid();
                        var ifcExtrudedAreaSolid = ifcRepresentationItem as IfcExtrudedAreaSolid;
                        entityInfo.Solid.Height = (double) ifcExtrudedAreaSolid.Depth.Value*scale;

                        var ifcProfile = ifcExtrudedAreaSolid.SweptArea;
                        if (ifcProfile is IfcCircleHollowProfileDef)
                        {
                            var circleProfile = ifcProfile as IfcCircleHollowProfileDef;
                            entityInfo.Solid.Size = new[] {circleProfile.Radius*scale2};
                            entityInfo.Solid.Thickness = circleProfile.WallThickness;
                            entityInfo.Solid.Type = 0;
                        }
                        else if (ifcProfile is IfcRectangleHollowProfileDef)
                        {
                            var rectangleProfile = ifcProfile as IfcRectangleHollowProfileDef;
                            entityInfo.Solid.Size = new[] {rectangleProfile.XDim*scale2, rectangleProfile.YDim*scale3};
                            entityInfo.Solid.Thickness = rectangleProfile.WallThickness;
                            entityInfo.Solid.Type = 1;
                        }
                        else if (ifcProfile is IfcCircleProfileDef)
                        {
                            var circleProfile = ifcProfile as IfcCircleProfileDef;
                            entityInfo.Solid.Size = new[] {circleProfile.Radius*scale2};
                            entityInfo.Solid.Thickness = 0.1;
                            entityInfo.Solid.Type = 0;
                        }
                        else if (ifcProfile is IfcRectangleProfileDef)
                        {
                            var rectangleProfile = ifcProfile as IfcRectangleProfileDef;
                            entityInfo.Solid.Size = new[] {rectangleProfile.XDim*scale2, rectangleProfile.YDim*scale3};
                            entityInfo.Solid.Thickness = 0.1;
                            entityInfo.Solid.Type = 1;
                        }
                        else
                        {
                            Console.WriteLine("Unknown SweptArea: " + ifcProfile.GetType().Name);
                            Console.ReadKey();
                        }
                    }
                    else
                    {
                        Console.WriteLine("Unknown IfcRepresentationItemType: " + ifcRepresentationItem.GetType().Name);
                        Console.ReadKey();
                    }
                }
                if (_systemDict.ContainsKey(entity.GlobalId))
                {
                    entityInfo.SystemInfo = _systemDict[entity.GlobalId];
                }
                else
                {
                    Console.WriteLine("{0} hasn't system info", entity.GlobalId);
                }
                result.Add(new string[] {entity.GlobalId, entity.GetType().Name}, entityInfo);
            }

            //foreach (var item in result)
            //{
            //    IfcSystem system = null;
            //    foreach (IfcProduct entity in entities)
            //    {
            //        if (!entity.GlobalId.ToString().Equals(item.Key[0])) continue;

            //        if (entity.HasAssignments.Any() && entity.HasAssignments.First() is IfcRelAssignsToGroup)
            //        {
            //            var assigment = (IfcRelAssignsToGroup)entity.HasAssignments.First();
            //            system = (IfcSystem)assigment.RelatingGroup;
            //        }
            //    }
            //    if (system != null && _systemDict.ContainsKey(system))
            //    {
            //        item.Value.SystemInfo = _systemDict[system];
            //    }
            //}

            return result;
        }

        private static List<string> GetEntityProperties(IfcProduct entity)
        {
            var result = new List<string>();
            List<IfcPropertySet> sets = entity.PropertySets.ToList();
            var systemInfo = new MagicSystem();
            //IfcSystem system = null;
            //if (entity.HasAssignments.Any() && entity.HasAssignments.First() is IfcRelAssignsToGroup)
            //{
            //    var assigment = (IfcRelAssignsToGroup)entity.HasAssignments.First();
            //    system = (IfcSystem)assigment.RelatingGroup;
            //}

            foreach (var set in sets)
            {
                var properties = set.HasProperties;
                foreach (var prop in properties)
                {
                    if (prop is IfcPropertySingleValue)
                    {
                        var singleValue = prop as IfcPropertySingleValue;
                        var propertyName = singleValue.Name.Value.ToString().Trim();
                        var propertyValue = singleValue.NominalValue.Value.ToString().Trim();
                        double dValue;
                        if (string.IsNullOrWhiteSpace(propertyValue) || propertyValue == "-" ||
                            (double.TryParse(
                                propertyValue.Replace(".", DecimalSeparator).Replace(",", DecimalSeparator), out dValue) &&
                             dValue == 0.0)) continue;
                        if (propertyName.Equals(MagicAttributes.PdmsSv2))
                        {
                            systemInfo.SubSystemCode = propertyValue.ToUpper();
                        }
                        else if (propertyName.Equals(MagicAttributes.PdmsSv3))
                        {
                            systemInfo.SubSystemIndex = propertyValue.ToUpper();
                        }
                        else if (propertyName.Equals(MagicAttributes.PdmsSv4))
                        {
                            systemInfo.SystemCode = propertyValue.ToUpper();
                        }
                        else if (propertyName.Equals(MagicAttributes.PdmsSystem2))
                        {
                            systemInfo.SystemType = "DUCT";
                            var apIndex = propertyValue.IndexOf("\"");
                            apIndex = apIndex < 0 ? 0 : apIndex;
                            systemInfo.SystemDescription =
                                propertyValue.Substring(apIndex)
                                    .Replace("\"", string.Empty);
                        }
                        else if (propertyName.Equals(MagicAttributes.PdmsSystem3))
                        {
                            systemInfo.SystemType = "PIPE";
                            var apIndex = propertyValue.IndexOf("\"");
                            apIndex = apIndex < 0 ? 0 : apIndex;
                            systemInfo.SystemDescription =
                                propertyValue.Substring(apIndex)
                                    .Replace("\"", string.Empty);
                        }
                        else
                        {
                            result.Add(propertyName + "#" + propertyValue);
                        }
                    }
                    else
                    {
                        Console.WriteLine(prop.EntityLabel);
                    }
                }
            }

            if (!_systemDict.ContainsKey(entity.GlobalId))
                _systemDict.Add(entity.GlobalId, systemInfo);

            //if (system != null && !_systemDict.ContainsKey(system) && !string.IsNullOrEmpty(systemInfo.SystemType))
            //{
            //    _systemDict.Add(system, systemInfo);
            //}

            result.Sort();
            return result;
        }
    }
}
