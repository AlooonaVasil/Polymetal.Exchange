using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Polymetal.Exchange.Utilities;
using Xbim.Common.Geometry;
using Xbim.Geometry.Engine.Interop;
using Xbim.Ifc2x3.Extensions;
using Xbim.Ifc2x3.GeometricModelResource;
using Xbim.Ifc2x3.GeometryResource;
using Xbim.Ifc2x3.ProductExtension;
using Xbim.Ifc2x3.ProfileResource;
using Xbim.Ifc2x3.PropertyResource;
using Xbim.Ifc2x3.RepresentationResource;
using Xbim.Ifc2x3.SharedBldgElements;
using Xbim.IO;
using XbimGeometry.Interfaces;

namespace Polymetal.Exchange.Revit
{
    internal class IfcParser
    {
        private static List<IfcRelFillsElement> _ifcRelFillsElementList = new List<IfcRelFillsElement>();
        private static string _ifcPath;
        internal static List<RevitElement> RunParse(string ifcPath)
        {
            _ifcPath = ifcPath;
            var revitElementsList = new List<RevitElement>();
            var ifcFile = new FileInfo(ifcPath);
            if (!ifcFile.Exists) return revitElementsList;
            var xBimPath = ifcFile.FullName.Replace(ifcFile.Extension, ".xBIM");
            Console.WriteLine("{0}: Обрабокта файла {1}", DateTime.Now.ToLongTimeString(), ifcPath);

            
            using (var model = new XbimModel())
            {
                if (model.CreateFrom(ifcFile.FullName, xBimPath))
                {
                    model.Open(xBimPath);
                    foreach (IfcRelFillsElement instance in Enumerable.OfType<IfcRelFillsElement>(model.Instances))
                    {
                        _ifcRelFillsElementList.Add(instance);
                    }

                    foreach (var entity in model.IfcProducts)
                    {
                        if (!(entity is IfcWall) && !(entity is IfcBeam) && !(entity is IfcSlab) &&
                            !(entity is IfcCovering) && !(entity is IfcRoof)) continue;
                        if (entity is IfcRoof && ((IfcElement)entity).IsDecomposedBy.Any())
                        {
                            continue;
                        }

                        var info = new RevitElement
                        {
                            FillElements = new List<RevitElement>(),
                            Id = (entity as IfcElement).GlobalId
                        };
                        if (AnalyzeEntity(entity as IfcElement, ref info))
                        {
                            revitElementsList.Add(info);
                        }
                        else
                        {
                            Console.WriteLine(entity.EntityLabel + "\tBad news in analyze entity");
                        }
                    }
                    model.Close();
                }
            }

            Console.WriteLine("{0}: IFC файл обработан {1}", DateTime.Now.ToLongTimeString(), ifcPath);
            if (File.Exists(xBimPath))
                File.Delete(xBimPath);

            return revitElementsList;
        }

        private static bool AnalyzeEntity(IfcElement ifcElement, ref RevitElement info)
        {
            if (ifcElement.Representation == null ||
                ifcElement.Representation.Representations.Count == 0) return false;

            List<IfcElement> voidElements, fillElements;
            GetOpeningElements(ifcElement, out voidElements, out fillElements);

            RepresentationType reprType;
            var itemRepresentationArray = GetItemRepresentation(ifcElement, voidElements, out reprType);
            if (itemRepresentationArray.Count == 0)
            {
                Console.WriteLine("null geometry in " + ifcElement.GetType().Name);
                return false;
            }
            info.Representations = itemRepresentationArray;
            info.RepresentationType = reprType;
            info.Properties = GetItemProperties(ifcElement);
            var fillIndex = 1;
            foreach (var fillElement in fillElements)
            {
                var fillInfo = new RevitElement
                {
                    FillElements = new List<RevitElement>(),
                    Id = info.Id+fillIndex//fillElement.GlobalId
                };
                if (AnalyzeEntity(fillElement, ref fillInfo))
                {
                    info.FillElements.Add(fillInfo);
                }
                else
                {
                    Console.WriteLine(ifcElement.EntityLabel + "\tBad news in analyze fill entity");
                }
                fillIndex++;
            }

            return true;
        }

        private static RevitProperties GetItemProperties(IfcElement ifcElement)
        {
            var properties = new RevitProperties { Desc = string.Empty, Elev = 0.0, Func = string.Empty };

            var dt = new DataTable();
            dt.Columns.Add("Group", typeof(string));
            dt.Columns.Add("Name", typeof(string));
            dt.Columns.Add("Value", typeof(string));

            var definingType = ifcElement.GetDefiningType(ifcElement.ModelOf);
            if (definingType != null)
            {
                var defProperties = definingType.GetAllPropertySingleValues();
                foreach (var groupPair in defProperties)
                {
                    var groupName = groupPair.Key.ToString();
                    foreach (var pair in groupPair.Value)
                    {
                        dt.Rows.Add(groupName, pair.Key.ToString(), pair.Value.Value.ToString());
                    }
                }
            }

            var ifcPropertySets = ifcElement.GetAllPropertySets();

            foreach (var ifcPropertySet in ifcPropertySets)
            {
                foreach (var ifcProperty in ifcPropertySet.HasProperties)
                {
                    if (!(ifcProperty is IfcPropertySingleValue)) continue;

                    var ifcPropertySingleValue = ifcProperty as IfcPropertySingleValue;
                    var propertyName = ifcPropertySingleValue.Name.Value.ToString().Trim();
                    var propertyValue = ifcPropertySingleValue.NominalValue.Value.ToString().Trim();
                    dt.Rows.Add(ifcPropertySet.Name.Value, propertyName, propertyValue);
                }
            }

            var rows =
                dt.Select("Group like '%зависимости%' and (Name like '%уровень%' or Name like '%Базовая зависимость%')");
            if (rows.Length == 1)
            {
                var value = rows[0]["Value"].ToString();
                value = value.Split("тм.".ToCharArray()).Last().Trim();
                double.TryParse(value.Replace(",", string.Empty), out properties.Elev);
            }

            var description = string.Empty;
            if (ifcElement is IfcBeam)
            {
                rows = dt.Select("Group like '%прочее%' and Name like '%имя семейства%'");
                if (rows.Length == 1)
                {
                    description = rows[0]["Value"].ToString().Trim() + " ";
                }
            }

            rows = dt.Select("Group like '%идентификация%' and Name like '%имя типа%'");
            if (rows.Length == 1)
            {
                var value = rows[0]["Value"].ToString().Trim();
                value = value.Replace("Внутренние - ", "").Replace("Наружный - ", "").Replace("Облицовка - ", "");
                properties.Desc = description + value;
            }

            rows = dt.Select("Group like '%Строительство%' and Name like '%Функция%'");
            if (rows.Length == 1)
            {
                var value = rows[0]["Value"].ToString().Trim();
                value = value.Replace("Наружные слои", "N").Replace("Внутренние слои", "V");
                properties.Func = value;
            }

            if (ifcElement is IfcWall)
            {
                properties.Type = ElementType.Wall;
            }
            else if (ifcElement is IfcBeam)
            {
                properties.Type = ElementType.Beam;
            }
            else if (ifcElement is IfcSlab)
            {
                properties.Type = ElementType.Slab;
            }
            else if (ifcElement is IfcDoor)
            {
                properties.Type = ElementType.Door;
            }
            else if (ifcElement is IfcWindow)
            {
                properties.Type = ElementType.Window;
            }
            else
            {
                properties.Type = ElementType.Roof;
            }

            return properties;
        }

        private static List<Representation> GetItemRepresentation(IfcElement ifcElement, List<IfcElement> voidElements,
            out RepresentationType reprType)
        {
            var result = new List<Representation>();
            bool geomExist;
            var itemMatrix = ifcElement.Transform();
            reprType = RepresentationType.Solid;
            foreach (var ifcRepresentation in ifcElement.Representation.Representations)
            {
                var ifcShapeRepresentation = ifcRepresentation as IfcShapeRepresentation;
                foreach (var ifcRepresentationItem in ifcShapeRepresentation.Items)
                {
                    if (ifcRepresentationItem is IfcBooleanClippingResult || ifcRepresentationItem is IfcFacetedBrep)
                    {
                        var geoList = GetFaceRepresentationGeometry(ifcRepresentationItem, itemMatrix, out geomExist);

                        if (geomExist)
                        {
                            reprType = TryConvertToSolid(ref geoList)
                                ? RepresentationType.Solid
                                : RepresentationType.Brep;
                            result.AddRange(geoList);
                        }
                    }
                    else if (ifcRepresentationItem is IfcMappedItem)
                    {
                        var ifcMappedItem = ifcRepresentationItem as IfcMappedItem;
                        var representationItems = ifcMappedItem.MappingSource.MappedRepresentation.Items;
                        foreach (var item in representationItems)
                        {
                            if (item is IfcPolyline) continue;
                           
                            //if (item is IfcFacetedBrep)
                            //{
                            //    var geoList = GetFaceRepresentationGeometry(item, itemMatrix, out geomExist);
                            //    //if (geoList.Count > 100) continue;
                            //    if (geomExist)
                            //    {
                            //        reprType = TryConvertToSolid(ref geoList)
                            //            ? RepresentationType.Solid
                            //            : RepresentationType.Brep;
                            //        result.AddRange(geoList);
                            //    }
                            //    continue;
                            //}

                            var geo = GetRepresentationGeometry(item, itemMatrix, out geomExist);
                            if (geomExist)
                                result.Add(geo);
                        }
                    }
                    else
                    {
                        if (ifcRepresentationItem is IfcPolyline) continue;
                        var geo = GetRepresentationGeometry(ifcRepresentationItem, itemMatrix, out geomExist);
                        if (geomExist)
                            result.Add(geo);
                    }
                }
            }

            var negativGeometry = new List<Geometry>();
            foreach (var voidElement in voidElements)
            {
                RepresentationType nReprType;
                var nRepr = GetItemRepresentation(voidElement, new List<IfcElement>(), out nReprType);
                negativGeometry.AddRange(nRepr.Select(n => n.Positive));
            }

            if (negativGeometry.Count <= 0) return result;
            if (result.Count == 1)
            {
                if (result[0].Negativ == null)
                {
                    result[0].Negativ = negativGeometry;
                }
                else
                {
                    result[0].Negativ.AddRange(negativGeometry);
                }
            }
            else
            {
                result.Add(new Representation() { Negativ = negativGeometry });
            }

            return result;
        }

        private static bool TryConvertToSolid(ref List<Representation> geoList)
        {
            for (var i = 0; i < geoList.Count; i++)
            {
                var face = geoList[i].Positive as Extrusion;

                var fPoint = face.Points[0];
                var tm = new XbimMatrix3D { OffsetX = fPoint.X, OffsetY = fPoint.Y, OffsetZ = fPoint.Z };
                tm.Invert();

                var vp = tm.Transform(face.Points[1]);

                var zVector = face.Normal;

                var yVector = new XbimVector3D(vp.X, vp.Y, vp.Z);
                yVector.Normalize();

                var xVector = yVector.CrossProduct(zVector);
                xVector.Normalize();

                var matrix = new XbimMatrix3D(xVector.X, xVector.Y, xVector.Z, 0.0, yVector.X, yVector.Y, yVector.Z, 0.0,
                    zVector.X, zVector.Y, zVector.Z, 0.0, fPoint.X, fPoint.Y, fPoint.Z, 1.0);
                matrix.Invert();

                var allPointList = new List<XbimPoint3D>();
                var depth = 0.0;
                foreach (var geo in geoList)
                {
                    var f = geo.Positive as Extrusion;
                    foreach (var p in f.Points)
                    {
                        var tp = matrix.Transform(p);
                        depth = Math.Max(depth, tp.Z);
                        tp.Z = 0.0;
                        if (allPointList.Contains(tp)) continue;

                        allPointList.Add(tp);
                    }
                }

                if (allPointList.Count == face.Points.Count)
                {
                    var extrudeGeometry = new Extrusion
                    {
                        Length = Math.Round(depth),
                        Points = face.Points,
                        Normal = face.Normal
                    };
                    geoList = new List<Representation> { new Representation { Positive = extrudeGeometry } };
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Получение элементов IfcOpening (негативы в текущем элементе и его заполнение другим элементом)
        /// </summary>
        /// <param name="element"></param>
        /// <param name="voidElements">вырезы</param>
        /// <param name="fillElements">субэлементы</param>
        private static void GetOpeningElements(IfcElement element, out List<IfcElement> voidElements,
            out List<IfcElement> fillElements)
        {
            voidElements = new List<IfcElement>();
            fillElements = new List<IfcElement>();
            foreach (var ifcRelVoidsElement in element.HasOpenings)
            {
                voidElements.Add(ifcRelVoidsElement.RelatedOpeningElement);
                foreach (
                    var ifcRelFillsElement in
                        _ifcRelFillsElementList.Where(
                            x => x.RelatingOpeningElement == ifcRelVoidsElement.RelatedOpeningElement))
                {
                    fillElements.Add(ifcRelFillsElement.RelatedBuildingElement);
                    break;
                }
            }
        }

        private static List<Representation> GetFaceRepresentationGeometry(IfcRepresentationItem ifcRepresentationItem,
            XbimMatrix3D globalMatrix, out bool geometryExist)
        {
            var result = new List<Representation>();
            geometryExist = false;
            var geometryEngine = new XbimGeometryEngine();
            var geometry = geometryEngine.Create(ifcRepresentationItem as IfcGeometricRepresentationItem);
            if (geometry.GeometryType == XbimGeometryObjectType.XbimSolidType)
            {
                var solid = geometry as IXbimSolid;
                foreach (var face in solid.Faces)
                {
                    var normal = globalMatrix.Transform(face.Normal);
                    normal.Negate();
                    var revitFace = new Extrusion
                    {
                        Points = new List<XbimPoint3D>(),
                        Normal = normal
                    };
                    foreach (var p in face.OuterBound.Points)
                    {
                        revitFace.Points.Add(FixPoint(globalMatrix.Transform(p)));
                    }
                    var repr = new Representation { Positive = revitFace };
                    result.Add(repr);
                }
                geometryExist = true;
            }
            else if (geometry.GeometryType == XbimGeometryObjectType.XbimSolidSetType)
            {
                var solidSet = geometry as IXbimSolidSet;
                foreach (var solid in solidSet)
                {
                    foreach (var face in solid.Faces)
                    {
                        var normal = globalMatrix.Transform(face.Normal);
                        normal.Negate();
                        var revitFace = new Extrusion
                        {
                            Points = new List<XbimPoint3D>(),
                            Normal = normal
                        };
                        foreach (var p in face.OuterBound.Points)
                        {
                            revitFace.Points.Add(FixPoint(globalMatrix.Transform(p)));
                        }
                        var repr = new Representation { Positive = revitFace };
                        result.Add(repr);
                    }
                }
                geometryExist = true;
            }
            else if (geometry.GeometryType == XbimGeometryObjectType.XbimGeometryObjectSetType)
            {
                var solid = geometry as IXbimGeometryObjectSet;
                foreach (var face in solid.Faces)
                {
                    var normal = globalMatrix.Transform(face.Normal);
                    normal.Negate();
                    var revitFace = new Extrusion
                    {
                        Points = new List<XbimPoint3D>(),
                        Normal = normal
                    };
                    foreach (var p in face.OuterBound.Points)
                    {
                        revitFace.Points.Add(FixPoint(globalMatrix.Transform(p)));
                    }

                    var negatives = new List<Geometry>();
                    foreach (var innerBound in face.InnerBounds)
                    {
                        var negativFace = new Extrusion
                        {
                            Points = new List<XbimPoint3D>(),
                            Normal = normal
                        };
                        foreach (var p in innerBound.Points)
                        {
                            negativFace.Points.Add(FixPoint(globalMatrix.Transform(p)));
                        }
                        negatives.Add(negativFace);
                    }
                    var repr = new Representation { Positive = revitFace };
                    if (negatives.Count > 0)
                    {
                        repr.Negativ = negatives;
                    }
                    result.Add(repr);
                }
                geometryExist = true;
            }
            else
            {
                Console.WriteLine("XbimGeometryObject.GeometryType is " + geometry.GeometryType);
            }
            return result;
        }

        private static XbimPoint3D FixPoint(XbimPoint3D point)
        {
            var result = new XbimPoint3D();
            var delta = 0.01;

            point.X = point.X.Equals(double.NaN) ? 0.0 : Math.Round(point.X, 4);
            point.Y = point.Y.Equals(double.NaN) ? 0.0 : Math.Round(point.Y, 4);
            point.Z = point.Z.Equals(double.NaN) ? 0.0 : Math.Round(point.Z, 4);

            result.X = Math.Ceiling(point.X) - point.X < delta
                ? Math.Ceiling(point.X)
                : (point.X - Math.Floor(point.X) < delta ? Math.Floor(point.X) : point.X);

            result.Y = Math.Ceiling(point.Y) - point.Y < delta
                ? Math.Ceiling(point.Y)
                : (point.Y - Math.Floor(point.Y) < delta ? Math.Floor(point.Y) : point.Y);

            result.Z = Math.Ceiling(point.Z) - point.Z < delta
                ? Math.Ceiling(point.Z)
                : (point.Z - Math.Floor(point.Z) < delta ? Math.Floor(point.Z) : point.Z);

            return result;
        }

        private static Representation GetRepresentationGeometry(IfcRepresentationItem ifcRepresentationItem,
            XbimMatrix3D globalMatrix, out bool geometryExist)
        {
            var result = new Representation();
            geometryExist = false;

            if (ifcRepresentationItem is IfcExtrudedAreaSolid &&
                (ifcRepresentationItem as IfcExtrudedAreaSolid).SweptArea is IfcArbitraryClosedProfileDef)
            {
                var ifcExtrudedAreaSolid = ifcRepresentationItem as IfcExtrudedAreaSolid;

                var ifcExtrudedAreaSolidMatrix = ifcExtrudedAreaSolid.Position.ToMatrix3D();
                var ifcArbitraryClosedProfileDef = ifcExtrudedAreaSolid.SweptArea as IfcArbitraryClosedProfileDef;

                var locationPoint =
                    FixPoint(globalMatrix.Transform(ifcExtrudedAreaSolidMatrix.Transform(new XbimPoint3D())));

                var zDirection = globalMatrix.Transform(ifcExtrudedAreaSolidMatrix.Transform(new XbimVector3D(0, 0, 1)));

                if (ifcArbitraryClosedProfileDef.OuterCurve is IfcPolyline)
                {
                    var extrude = ifcExtrudedAreaSolid.ExtrudedDirection.Z < 0 ? -1 : 1;
                    var extrudeGeometry = new Extrusion
                    {
                        Length = Math.Round(ifcExtrudedAreaSolid.Depth),
                        Points = new List<XbimPoint3D>(),
                        Normal = zDirection * extrude
                    };

                    var ifcPolyline = ifcArbitraryClosedProfileDef.OuterCurve as IfcPolyline;
                    var points = ifcPolyline.Points.ToList();
                    foreach (var point in points)
                    {
                        extrudeGeometry.Points.Add(
                            FixPoint(
                                globalMatrix.Transform(
                                    ifcExtrudedAreaSolidMatrix.Transform(FixPoint(point.XbimPoint3D())))));
                    }
                    geometryExist = true;
                    result.Positive = extrudeGeometry;
                }
                else if (ifcArbitraryClosedProfileDef.OuterCurve is IfcCompositeCurve)
                {
                    var ifcCompositeCurve = ifcArbitraryClosedProfileDef.OuterCurve as IfcCompositeCurve;
                    foreach (var segment in ifcCompositeCurve.Segments)
                    {
                        var parentCurve = segment.ParentCurve;

                        if (parentCurve is IfcPolyline) continue;

                        if (parentCurve is IfcTrimmedCurve)
                        {
                            var ifcTrimmedCurve = parentCurve as IfcTrimmedCurve;
                            var basicCurve = ifcTrimmedCurve.BasisCurve;
                            if (basicCurve is IfcCircle)
                            {
                                var ifcCircle = basicCurve as IfcCircle;
                                var extrude = ifcExtrudedAreaSolid.ExtrudedDirection.Z < 0 ? -1 : 1;
                                var circleGeometry = new Circle
                                {
                                    Radius = Math.Round(ifcCircle.Radius, 1),
                                    Length = Math.Round(ifcExtrudedAreaSolid.Depth),
                                    Normal = zDirection * extrude,
                                    Position = locationPoint
                                };
                                geometryExist = true;
                                result.Positive = circleGeometry;
                            }
                            else
                            {
                                Console.WriteLine("IfcTrimmedCurve.BasisCurve is " +
                                                  ifcTrimmedCurve.BasisCurve.GetType().Name);
                            }
                        }
                        else
                        {
                            Console.WriteLine("ParentCurve is " + parentCurve.GetType().Name);
                        }
                    }
                }
                else
                {
                    Console.WriteLine("IfcArbitraryClosedProfileDef.OuterCurve is " +
                                      ifcArbitraryClosedProfileDef.OuterCurve.GetType().Name);
                }

                if (ifcArbitraryClosedProfileDef is IfcArbitraryProfileDefWithVoids)
                {
                    var ifcArbitraryProfileDefWithVoids =
                        ifcArbitraryClosedProfileDef as IfcArbitraryProfileDefWithVoids;
                    var negativGeometry = new List<Geometry>();
                    foreach (var innerCurve in ifcArbitraryProfileDefWithVoids.InnerCurves)
                    {
                        if (innerCurve is IfcPolyline)
                        {
                            var extrude = ifcExtrudedAreaSolid.ExtrudedDirection.Z < 0 ? -1 : 1;
                            var extrudeGeometry = new Extrusion
                            {
                                Length = Math.Round(ifcExtrudedAreaSolid.Depth),
                                Points = new List<XbimPoint3D>(),
                                Normal = zDirection * extrude
                            };

                            var ifcPolyline = innerCurve as IfcPolyline;
                            var points = ifcPolyline.Points.ToList();
                            foreach (var point in points)
                            {
                                extrudeGeometry.Points.Add(
                                    FixPoint(
                                        globalMatrix.Transform(
                                            ifcExtrudedAreaSolidMatrix.Transform(FixPoint(point.XbimPoint3D())))));
                            }
                            negativGeometry.Add(extrudeGeometry);
                        }
                        else
                        {
                            Console.WriteLine("IfcArbitraryClosedProfileDef.OuterCurve is " +
                                              innerCurve.GetType().Name);
                        }

                        if (negativGeometry.Count > 0)
                        {
                            result.Negativ = negativGeometry;
                        }
                    }
                }
            }
            else if (ifcRepresentationItem is IfcExtrudedAreaSolid &&
                     (ifcRepresentationItem as IfcExtrudedAreaSolid).SweptArea is IfcRectangleProfileDef)
            {
                var ifcExtrudedAreaSolid = ifcRepresentationItem as IfcExtrudedAreaSolid;
                var ifcExtrudedAreaSolidMatrix = ifcExtrudedAreaSolid.Position.ToMatrix3D();
                var ifcRectangleProfileDef = ifcExtrudedAreaSolid.SweptArea as IfcRectangleProfileDef;
                var ifcRectangleProfileDefMatrix = ifcRectangleProfileDef.Position.ToMatrix3D();

                var zDirection =
                    globalMatrix.Transform(
                        ifcExtrudedAreaSolidMatrix.Transform(
                            ifcRectangleProfileDefMatrix.Transform(new XbimVector3D(0, 0, 1))));

                var xDim = ifcRectangleProfileDef.XDim / 2;
                var yDim = ifcRectangleProfileDef.YDim / 2;
                var points = new List<XbimPoint3D>
                {
                    FixPoint(FixPoint(
                        globalMatrix.Transform(
                            ifcExtrudedAreaSolidMatrix.Transform(
                                ifcRectangleProfileDefMatrix.Transform(new XbimPoint3D(xDim, yDim, 0)))))),
                    FixPoint(FixPoint(
                        globalMatrix.Transform(
                            ifcExtrudedAreaSolidMatrix.Transform(
                                ifcRectangleProfileDefMatrix.Transform(new XbimPoint3D(-xDim, yDim, 0)))))),
                    FixPoint(FixPoint(
                        globalMatrix.Transform(
                            ifcExtrudedAreaSolidMatrix.Transform(
                                ifcRectangleProfileDefMatrix.Transform(new XbimPoint3D(-xDim, -yDim, 0)))))),
                    FixPoint(FixPoint(
                        globalMatrix.Transform(
                            ifcExtrudedAreaSolidMatrix.Transform(
                                ifcRectangleProfileDefMatrix.Transform(new XbimPoint3D(xDim, -yDim, 0)))))),
                };

                var extrude = ifcExtrudedAreaSolid.ExtrudedDirection.Z < 0 ? -1 : 1;
                var rectangleGeometry = new Extrusion
                {
                    Points = points,
                    Length = Math.Round(ifcExtrudedAreaSolid.Depth),
                    Normal = zDirection * extrude
                };

                result.Positive = rectangleGeometry;
                geometryExist = true;
            }
            else if (ifcRepresentationItem is IfcExtrudedAreaSolid &&
                     (ifcRepresentationItem as IfcExtrudedAreaSolid).SweptArea is IfcCircleProfileDef)
            {
                var ifcExtrudedAreaSolid = ifcRepresentationItem as IfcExtrudedAreaSolid;
                var ifcExtrudedAreaSolidMatrix = ifcExtrudedAreaSolid.Position.ToMatrix3D();
                var ifcCircleProfileDef = ifcExtrudedAreaSolid.SweptArea as IfcCircleProfileDef;
                var ifcCircleProfileDefMatrix = ifcCircleProfileDef.Position.ToMatrix3D();

                var locationPoint =
                    FixPoint(
                        globalMatrix.Transform(
                            ifcExtrudedAreaSolidMatrix.Transform(ifcCircleProfileDefMatrix.Transform(new XbimPoint3D()))));

                var zDirection =
                    globalMatrix.Transform(
                        ifcExtrudedAreaSolidMatrix.Transform(
                            ifcCircleProfileDefMatrix.Transform(new XbimVector3D(0, 0, 1))));

                var extrude = ifcExtrudedAreaSolid.ExtrudedDirection.Z < 0 ? -1 : 1;
                var circleGeometry = new Circle
                {
                    Radius = Math.Round(ifcCircleProfileDef.Radius, 1),
                    Length = Math.Round(ifcExtrudedAreaSolid.Depth),
                    Normal = zDirection * extrude,
                    Position = locationPoint
                };

                result.Positive = circleGeometry;
                geometryExist = true;
            }
            else if (ifcRepresentationItem is IfcGeometricSet)
            {
                var ifcGeometricSet = ifcRepresentationItem as IfcGeometricSet;
                foreach (var item in ifcGeometricSet.Elements.Where(item => !(item is IfcPolyline)))
                {
                    Console.WriteLine("IfcGeometricSet.Element is " + item.GetType().Name);
                }
            }
            else
            {
                Console.WriteLine("IfcRepresentationItem is " + ifcRepresentationItem.GetType().Name);
            }

            return result;
        }
    }
}
