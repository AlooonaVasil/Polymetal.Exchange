using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aveva.Pdms.Database;
using Aveva.Pdms.Geometry;
using Aveva.Pdms.Maths.Geometry;
using Aveva.Pdms.CSG;
using Aveva.Pdms.Utilities.Messaging;
using Orientation = Aveva.Pdms.Geometry.Orientation;

namespace Polymetal.Pdms.Ifc
{
    internal class PdmsGeometry
    {
        internal double[] Matrix;
        internal int Translucency;
        internal int Hash;
        internal int Color;
        internal List<PdmsGeometry> Negatives = new List<PdmsGeometry>();
        internal DbElement Root;

        internal PdmsGeometry()
        {
        }

        internal PdmsGeometry(DbElement root)
        {
            Root = root;
        }

        internal StringBuilder GetText()
        {
            var geometries = new List<PdmsGeometry>();
            if (Root.GetElementType() == DbElementTypeInstance.EQUIPMENT)
            {
                var geomList = GetEquiGeometry(Root);
                //если элемент MagiCAD, преобразовать к FacetGroup
                if (Root.GetAsString(DbAttributeInstance.FUNC) == "MagiCAD")
                {
                    var asFaceExtrusions = new List<DbElement>();
                    var faces = new Dictionary<int,List<List<double[]>>>();
                    foreach (var geom in geomList)
                    {
                        if (geom.GetElementType() != DbElementTypeInstance.EXTRUSION || geom.GetDouble(DbAttributeInstance.HEIG) >= 10.0) continue;
                        var f = new List<double[]>();
                        foreach (var loop in geom.Members(DbElementTypeInstance.LOOP))
                        {
                            var vertexes = loop.Members();
                            if (vertexes.Length < 3) break;
                            foreach (var vert in vertexes)
                            {
                                var pos = vert.GetPosition(DbAttributeInstance.POS,0,Root);
                                f.Add(new []{pos.X,pos.Y,pos.Z});
                            }

                            var p1 = vertexes[0].GetPosition(DbAttributeInstance.WORPOS);
                            var p2 = vertexes[1].GetPosition(DbAttributeInstance.WORPOS);
                            var p3 = vertexes[2].GetPosition(DbAttributeInstance.WORPOS);
                            var vx = Direction.Create(p2, p1);
                            var vy = Direction.Create(p2, p3);
                            var vz = vx.Orthogonal(vy);

                            var extrDirection = geom.GetOrientation(DbAttributeInstance.WORI).ZDir();
                            var angle = extrDirection.Angle(vz);
                            if (angle > 1)
                            {
                                f.Reverse();
                            }

                        }
                        if (f.Count > 0)
                        {
                            var tran = GetTranslucency(geom);
                            if (!faces.ContainsKey(tran))
                                faces.Add(tran, new List<List<double[]>>());
                            faces[tran].Add(f);
                        }
                        asFaceExtrusions.Add(geom);
                    }
                    foreach (var extr in asFaceExtrusions)
                        geomList.Remove(extr);
                    foreach (var pair in faces)
                    {
                        if (pair.Value.Count == 0) continue;
                        var pdmsGeometry = new PdmsFacetGroup(pair.Value, GetMatrix(Root));
                        pdmsGeometry.Color = GetItemColor(Root);
                        pdmsGeometry.Translucency = pair.Key;
                        geometries.Add(pdmsGeometry);
                    }
                }
                foreach (var geom in geomList)
                {
                    if (geom.GetElementType() == DbElementTypeInstance.NOZZLE ||
                        geom.GetElementType() == DbElementTypeInstance.ELCONN ||
                        geom.GetElementType() == DbElementTypeInstance.SCTN ||
                        geom.Owner.GetElementType() == DbElementTypeInstance.BRANCH)
                    {
                        geometries.AddRange(GetCatalogueGeometry(geom));
                        continue;
                    }

                    //todo: доделать
                    if (geom.GetElementType() == DbElementTypeInstance.GENSEC) continue;

                    if (geom.GetElementType() == DbElementTypeInstance.GENPRI || geom.GetElementType() == DbElementTypeInstance.GENCUR)
                    {
                        List<List<List<double[]>>> faceSets;
                        List<double[]> matrixes;
                        GetMeshRepresentation(geom, out faceSets, out matrixes);
                        for (var i = 0; i < faceSets.Count; i++)
                        {
                            var pdmsFacetGroup = new PdmsFacetGroup(faceSets[i], matrixes[i]);
                            pdmsFacetGroup.Color = GetItemColor(geom);
                            pdmsFacetGroup.Translucency = GetTranslucency(geom);
                            geometries.Add(pdmsFacetGroup);
                        }
                        continue;
                    }

                    var pdmsGeometry = GetItemGeometry(geom, false);
                    pdmsGeometry.Color = GetItemColor(geom);
                    if (pdmsGeometry is PdmsExtrusion)
                    {
                        var profiles = GetProfilePoints(geom);
                        var pdmsExtrusion = pdmsGeometry as PdmsExtrusion;
                        if (profiles.Count == 1)
                        {
                            pdmsExtrusion.Profile = profiles[0];
                            geometries.Add(pdmsExtrusion);
                        }
                        else
                        {
                            foreach (var profile in profiles)
                            {
                                var extrusionGeometry = new PdmsExtrusion(profile, pdmsExtrusion.Height,
                                    pdmsExtrusion.Matrix, pdmsExtrusion.Negatives)
                                {
                                    Color = pdmsGeometry.Color,
                                    Translucency = pdmsGeometry.Translucency
                                };
                                geometries.Add(extrusionGeometry);
                            }
                        }
                        continue;
                    }

                    if (pdmsGeometry is PdmsRevolution)
                    {
                        var profiles = GetProfilePoints(geom);
                        var pdmsRevolution = pdmsGeometry as PdmsRevolution;
                        if (profiles.Count == 1)
                        {
                            pdmsRevolution.Profile = profiles[0];
                            geometries.Add(pdmsRevolution);
                        }
                        else
                        {
                            foreach (var profile in profiles)
                            {
                                var revolutionGeometry = new PdmsRevolution(profile, pdmsRevolution.Angle,
                                    pdmsRevolution.Matrix, pdmsRevolution.Negatives)
                                {
                                    Color = pdmsGeometry.Color,
                                    Translucency = pdmsGeometry.Translucency
                                };
                                geometries.Add(revolutionGeometry);
                            }
                        }
                        continue;
                    }
                    geometries.Add(pdmsGeometry);
                }
            }
            else
            {
                geometries.AddRange(GetCatalogueGeometry(Root));
            }

            var sb = new StringBuilder();
            if (geometries.Count == 0) return sb;
            var matrix = GetMatrix(Root); 
            sb.AppendLine(string.Join(" ", matrix.Select(x=>x.ToString("0.0000")).ToArray()));
           
            foreach (var g in geometries)
            {
                sb.Append(g);
                foreach (var negativ in g.Negatives)
                {
                    sb.Append("#");
                    sb.Append(negativ);
                }
                sb.AppendLine();
                g.Negatives.Clear();
            }
            return sb;
        }
        private List<List<double[]>> GetProfilePoints(DbElement element)
        {
            var result = new List<List<double[]>>();
            var opt = CSGTreeBuilder.Instance.CreateCSGTreeBuilderOptions();
            opt.Pline = false;
            opt.Holes = false;
            var csg = CSGTreeBuilder.Instance.GetGeometry(element, opt);
            if (csg.Length == 0) return result;
            var item = csg[0];
            var csgItem = item.Items[0];
            if (csgItem is CSGFacetGroup)
            {
                var list = new List<List<D3Point>>();
                var facetGroup = csgItem as CSGFacetGroup;
                foreach (var facet in facetGroup.Facets)
                {
                    foreach (var loop in facet.Loops)
                    {
                        if (loop.Vertices.Any(v => v.Z > 0)) continue;
                        var subList = new List<D3Point>();

                        foreach (var vert in loop.Vertices)
                        {
                            var flag = false;
                            foreach (var v in subList)
                            {
                                if (!D3Line.Create(v, vert).CheckLength(0.5))
                                {
                                    flag = true;
                                    break;
                                }
                            }
                            if (flag) continue;
                            subList.Add(vert);
                        }
                        if (subList.Count < 3) continue;
                        list.Add(subList);
                    }
                }

                foreach (var face in list)
                {
                    var profilePoints = new List<double[]>();
                    foreach (var vert in face)
                    {
                        profilePoints.Add(new[]
                        {
                            Math.Round(vert.X, 4),
                            Math.Round(vert.Y, 4),
                            Math.Round(vert.Z, 4), 0.0
                        });
                    }
                    result.Add(profilePoints);
                }
            }

            return result;
        }

        private List<DbElement> GetEquiGeometry(DbElement root)
        {
            var res = new List<DbElement>();
            foreach (var member in root.Members())
            {
                var memberType = member.GetElementType();
                if (memberType == DbElementTypeInstance.TMPLATE ||
                    memberType == DbElementTypeInstance.SUBEQUIPMENT)
                {
                    res.AddRange(GetEquiGeometry(member));
                    continue;
                }

                if (memberType == DbElementTypeInstance.DPSET ||
                    memberType == DbElementTypeInstance.DDSE ||
                    memberType == DbElementTypeInstance.TMRPSET ||
                    memberType == DbElementType.GetElementType("AIDGROUP")) continue;
                res.Add(member);
            }
            return res;
        }

        internal PdmsGeometry GetItemGeometry(DbElement item,bool isNegative)
        {
            PdmsGeometry res = null;
            var type = item.GetElementType();
            if (type == DbElementTypeInstance.PYRAMID || type == DbElementTypeInstance.NPYRAMID)
                res = new PdmsPyramid(item);

            if (type == DbElementTypeInstance.BOX || type == DbElementTypeInstance.NBOX)
                res = new PdmsBox(item);

            if (type == DbElementTypeInstance.RTORUS || type == DbElementTypeInstance.NRTORUS)
                res = new PdmsRectTorus(item);

            if (type == DbElementTypeInstance.CTORUS || type == DbElementTypeInstance.NCTORUS)
                res = new PdmsCircTorus(item);

            if (type == DbElementTypeInstance.DISH || type == DbElementTypeInstance.NDISH)
            {
                if (item.GetDouble(DbAttributeInstance.RADI) > 0.0)
                    res = new PdmsEllipticalDish(item);
                else
                    res = new PdmsSphericalDish(item);
            }

            if (type == DbElementTypeInstance.SNOUT || type == DbElementTypeInstance.NSNOUT)
                res = new PdmsSnout(item);

            if (type == DbElementTypeInstance.SLCYLINDER || type == DbElementTypeInstance.NSLCYLINDER)
                res = new PdmsSlcy(item);

            if (type == DbElementTypeInstance.CONE || type == DbElementTypeInstance.NCONE)
                res = new PdmsCone(item);

            if (type == DbElementTypeInstance.CYLINDER || type == DbElementTypeInstance.NCYLINDER)
                res = new PdmsCylinder(item);

            if (type == DbElementTypeInstance.REVOLUTION || type == DbElementTypeInstance.NREVOLUTION)
                res = new PdmsRevolution(item);

            if (type == DbElementTypeInstance.EXTRUSION || type == DbElementTypeInstance.NXTRUSION)
                res = new PdmsExtrusion(item);

            if (res == null)
            {
                using (var sw = new StreamWriter(@"C:\AVEVA\exchange.error", true))
                    sw.WriteLine($"IfcGeometry for type {type.Name} not implemented");
                    throw new NotImplementedException();
            }

            if (!isNegative)
            {
                res.Translucency = GetTranslucency(item);
            }

            foreach (var negativeMember in item.Members())
            {
                if (negativeMember.GetElementType() == DbElementTypeInstance.LOOP) continue;
                var negative = GetItemGeometry(negativeMember, true);
                res.Negatives.Add(negative);
            }
            return res;
        }

        private static int GetTranslucency(DbElement element)
        {
            var levels = element.GetIntegerArray(DbAttributeInstance.LEVE);
            var obst = element.GetInteger(DbAttributeInstance.OBST);
            if (element.GetElementType() == DbElementTypeInstance.SCTN) obst = 0;
            return (ExportIfc.REPRE_LEVEL < levels[0] || ExportIfc.REPRE_LEVEL > levels[1]) && obst > 0 ? 75 : 0;
        }

        internal List<PdmsGeometry> GetCatalogueGeometry(DbElement item)
        {
            var color = GetItemColor(item);

            var res = new List<PdmsGeometry>();
            var type = item.GetElementType();
            if (type == DbElementTypeInstance.SCTN)
            {
                var sctnRepresentation = new List<PdmsGeometry>();
                var expr = ExportIfc.GetOrAddExpression("PURP OF SITE EQ 'AXES'");
                if (item.EvaluateBool(expr))
                {
                    foreach (var fitt in item.Members())
                    {
                        sctnRepresentation.AddRange(GetCatalogueRepresentation(fitt));
                    }
                }
                else
                {
                    sctnRepresentation = GetCatalogueRepresentation(item);
                }
                var negatives = new List<PdmsGeometry>();
                foreach (var fitt in item.Members())
                {
                    if (fitt.GetElementType() == DbElementTypeInstance.SNODE) continue;
                    foreach (var tmpl in fitt.Members())
                    {
                        foreach (var neg in tmpl.Members())
                        {
                            negatives.Add(GetItemGeometry(neg,true));
                        }
                    }
                }

                foreach (var repr in sctnRepresentation)
                    repr.Negatives.AddRange(negatives);

                res.AddRange(sctnRepresentation);

                //var profiles = GetSctnProfilePoints(item);
                //var poss = item.GetPosition(DbAttributeInstance.POSS);
                //var pose = item.GetPosition(DbAttributeInstance.POSE);
                //var length = poss.Distance(pose);

                //foreach (var profile in profiles)
                //{
                //    var pdmsExtrusion = new PdmsExtrusion(profile, length, GetMatrix(item), negatives);
                //    res.Add(pdmsExtrusion);
                //}
            }
            else if (type == DbElementTypeInstance.GWALL ||
                     type == DbElementTypeInstance.FLOOR ||
                     type == DbElementTypeInstance.PANEL)
            {
                var negatives = new List<PdmsGeometry>();
                foreach (var member in item.Members())
                {
                    if (member.GetElementType() == DbElementTypeInstance.PLOOP ||
                        member.GetElementType() == DbElementTypeInstance.CMPFITTING ||
                        member.GetElementType() == DbElementTypeInstance.TMPLATE) continue;
                    negatives.Add(GetItemGeometry(member,true));
                }
                if (item.Members(DbElementTypeInstance.PLOOP).Length > 0)
                {
                    var pdmsExtrusion = new PdmsExtrusion(item) {Negatives = negatives};
                    res.Add(pdmsExtrusion);
                }
                else
                {
                    var tmplates = item.Members(DbElementTypeInstance.TMPLATE);
                    if (tmplates.Length > 0)
                    {
                        var tmpl = tmplates.First();
                        var translucency = 0;
                        if (tmpl.Members(DbElementTypeInstance.GENPRI).Length > 0)
                        {
                            translucency = GetTranslucency(tmpl.Members(DbElementTypeInstance.GENPRI).First());
                        }
                        List<List<List<double[]>>> faceSets;
                        List<double[]> matrixes;
                        GetMeshRepresentation(tmpl, out faceSets, out matrixes);
                        for (var i = 0; i < faceSets.Count; i++)
                        {
                            var pdmsFacetGroup = new PdmsFacetGroup(faceSets[i], matrixes[i]);
                            pdmsFacetGroup.Translucency = translucency;
                            res.Add(pdmsFacetGroup);
                        }
                    }
                }

                foreach (var cmpfi in item.Members(DbElementTypeInstance.CMPFITTING))
                {
                    foreach (var sbfi in cmpfi.Members(DbElementTypeInstance.SBFITTING))
                    {
                        foreach (var tmpl in sbfi.Members())
                        {
                            foreach (var memb in tmpl.Members())
                            {
                                List<List<List<double[]>>> faceSets;
                                List<double[]> matrixes;
                                GetMeshRepresentation(memb, out faceSets, out matrixes);
                                for (var i = 0; i < faceSets.Count; i++)
                                {
                                    var pdmsFacetGroup = new PdmsFacetGroup(faceSets[i], matrixes[i]);
                                    pdmsFacetGroup.Translucency = GetTranslucency(memb);
                                    res.Add(pdmsFacetGroup);
                                }
                            }
                        }
                    }
                }
            }
            else if (item.Owner.GetElementType() == DbElementTypeInstance.BRANCH ||
                     type == DbElementTypeInstance.NOZZLE ||
                     type == DbElementTypeInstance.ELCONN)
            {
                //res.AddRange(GetCSGRepresentation(item));
                res.AddRange(GetCatalogueRepresentation(item));
            }
            else if (type == DbElementTypeInstance.CWBRAN)
            {
                res.AddRange(GetElecCSGRepresentation(item));
            }
            else if (type == DbElementTypeInstance.CABLE)
            {
                //var pointSet = item.GetElementArray(DbAttributeInstance.CBPREF);
            }

            foreach (var geom in res)
                geom.Color = color;

            return res;
        }

        internal int GetItemColor(DbElement root)
        {
            foreach (var rule in ExportIfc.ColorRules)
            {
                var item = DbElement.GetElement();
                var condition = false;
                if (!root.EvaluateValidElement(ExportIfc.GetOrAddExpression(rule[0]), ref item)) continue;
                if (item.IsNull) continue;
                if (!item.EvaluateValidBool(ExportIfc.GetOrAddExpression(rule[1]), ref condition)) continue;
                if (!condition) continue;

                return int.Parse(rule[2]);
            }
            return 0;
        }

        internal List<PdmsGeometry> GetCatalogueRepresentation(DbElement element)
        {
            var result = new Dictionary<int, PdmsGeometry>();
            var gmref = DbElement.GetElement();
            var gmAttrib = element.GetElementType() == DbElementTypeInstance.SCTN ? DbAttributeInstance.GSTR : DbAttributeInstance.GMRE;

            if (!element.GetElement(DbAttributeInstance.CATR).IsNull)
                gmref = element.GetElement(gmAttrib);

            if (gmref.IsNull)
            {
                if (element.GetElementType() == DbElementTypeInstance.BEND)
                {
                    return new List<PdmsGeometry> { new PdmsCircTorus(element) };
                }
                Console.WriteLine("null gmref of {0}" + element.GetAsString(DbAttributeInstance.FLNM));
            }

            foreach (var gmItem in gmref.Members())
            {
                var representation = GetCatalogueItemRepresentation(element, gmItem);
                if (representation == null) continue;
                if (result.ContainsKey(representation.Hash))
                {
                    result[representation.Hash].Translucency = Math.Min(result[representation.Hash].Translucency,
                        representation.Translucency);
                    continue;
                }
                result.Add(representation.Hash, representation);
            }
            return result.Values.ToList();
        }

        internal PdmsGeometry GetCatalogueItemRepresentation(DbElement element, DbElement gmItem, bool negative=false)
        {
            PdmsGeometry geom = null;

            var levels = gmItem.GetIntegerArray(DbAttributeInstance.LEVE);
            var obst = gmItem.GetInteger(DbAttributeInstance.OBST);
            if (element.GetElementType() == DbElementTypeInstance.SCTN) obst = 0;
            if ((ExportIfc.REPRE_LEVEL < levels[0] || ExportIfc.REPRE_LEVEL > levels[1]) && obst ==0) return null;
            var gmType = gmItem.GetElementType();
            if (gmType == DbElementTypeInstance.LINE) return null;


            if (gmType == DbElementTypeInstance.SCYLINDER || gmType == DbElementTypeInstance.NSCYLINDER ||
                gmType == DbElementTypeInstance.LCYLINDER || gmType == DbElementTypeInstance.NLCYLINDER ||
                gmType == DbElementTypeInstance.TUBE)
            {
                var cyliGeom = new PdmsCylinder(element, gmItem);
                if (cyliGeom.Height < 0.1) return null;
                geom = cyliGeom;
            }
            else if (gmType == DbElementTypeInstance.SSLCYLINDER)
            {
                geom = new PdmsSlcy(element, gmItem);
            }
            else if (gmType == DbElementTypeInstance.LSNOUT)
            {
                geom = new PdmsSnout(element, gmItem);
            }
            else if (gmType == DbElementTypeInstance.SCTORUS)
            {
                geom = new PdmsCircTorus(element, gmItem);
            }
            else if (gmType == DbElementTypeInstance.SBOX || gmType == DbElementTypeInstance.NSBOX || gmType == DbElementTypeInstance.SRECTANGLE)
            {
                geom = new PdmsBox(element, gmItem);
            }
            else if (gmType == DbElementTypeInstance.LPYRAMID)
            {
                geom = new PdmsPyramid(element, gmItem);
            }
            else if (gmType == DbElementTypeInstance.SDSH)
            {
                var isElipt = element.EvaluateDouble(ExportIfc.GetOrAddExpression(gmItem.GetAsString(DbAttributeInstance.PRAD)), DbAttributeUnit.NONE) > 0.0;
                if (isElipt)
                    geom = new PdmsEllipticalDish(element, gmItem);
                else
                    geom = new PdmsSphericalDish(element, gmItem);
            }
            else if (gmType == DbElementTypeInstance.SRTORUS)
            {
                geom = new PdmsRectTorus(element, gmItem);
            }
            else if (gmType == DbElementTypeInstance.SSPHERE)
            {
                geom = new PdmsSphere(element, gmItem);
            }
            else if (gmType == DbElementTypeInstance.SEXTRUSION || gmType == DbElementTypeInstance.SPROFILE)
            {
                geom = new PdmsExtrusion(element, gmItem);
            }

            if (geom != null && !negative)
            {
                var negatives = new Dictionary<int,PdmsGeometry>();
                foreach (var negGmItem in gmItem.Members())
                {
                    if (negGmItem.GetElementType() == DbElementTypeInstance.SPVERT ||
                        negGmItem.GetElementType() == DbElementTypeInstance.SLOOP) continue;
                    var representation = GetCatalogueItemRepresentation(element, negGmItem,true);
                    if (representation == null) continue;
                    if (negatives.ContainsKey(representation.Hash)) continue;
                    negatives.Add(representation.Hash,representation);
                }
                geom.Negatives = negatives.Values.ToList();
                geom.Translucency = GetTranslucency(gmItem);
            }
            return geom;
        }

        internal double[] GetMatrix(DbElement item)
        {
            if (item.GetElementType() == DbElementTypeInstance.CABLE)
                return new[]
                {
                    0.0, 0.0, 0.0,
                    0.0, 0.0, 0.0,
                    0.0, 0.0, 0.0,
                    0.0, 0.0, 0.0
                };

            var worpos = item.GetPosition(DbAttributeInstance.WORPOS);
            var wori = item.GetOrientation(DbAttributeInstance.WORI);
            var xdir = wori.XDir();
            var ydir = wori.YDir();
            var zdir = wori.ZDir();
            var m = new[]
            {
                xdir.East, xdir.North, xdir.Up,
                ydir.East, ydir.North, ydir.Up,
                zdir.East, zdir.North, zdir.Up,
                worpos.X,  worpos.Y, worpos.Z
            };

            if (item.GetElementType() == DbElementTypeInstance.FLOOR && item.Members(DbElementTypeInstance.PLOOP).Length > 0)
            {                
                var ploo = item.Members(DbElementTypeInstance.PLOOP).First();
                var sjus = ploo.GetAsString(DbAttributeInstance.SJUS).ToUpper();
                if (ploo.GetAsString(DbAttributeInstance.SJUS).ToUpper() == "UCEN")
                {
                    var height = ploo.GetDouble(DbAttributeInstance.HEIG);
                    var transform = D3Transform.Create(m);
                    var transformPoint = transform.Multiply(D3Point.Create(0, 0, -height / 2));
                    m[9] = transformPoint.X;
                    m[10] = transformPoint.Y;
                    m[11] = transformPoint.Z;
                }
                else if (sjus != "UNSET")
                {
                    Console.WriteLine($"{item} has SJUS {sjus}");
                }
            }

            return m;
        }

        internal double[] GetMatrix(DbElement root, Orientation orientation, Position position)
        {
            var rootMatrix = D3Transform.Create(GetMatrix(root));
            var xdir = orientation.XDir();
            var ydir = orientation.YDir();
            var zdir = orientation.ZDir();
            var xvector = rootMatrix.Multiply(D3Vector.Create(xdir.East, xdir.North, xdir.Up));
            var yvector = rootMatrix.Multiply(D3Vector.Create(ydir.East, ydir.North, ydir.Up));
            var zvector = rootMatrix.Multiply(D3Vector.Create(zdir.East, zdir.North, zdir.Up));
            var origin = rootMatrix.Multiply(D3Point.Create(position.X, position.Y, position.Z));

            return new[]
            {
                xvector.X,xvector.Y,xvector.Z,
                yvector.X,yvector.Y,yvector.Z,
                zvector.X,zvector.Y,zvector.Z,
                origin.X, origin.Y, origin.Z
            };
        }

        //internal List<List<double[]>> GetSctnProfilePoints(DbElement element)
        //{
        //    var result = new List<List<double[]>>();
        //    var opt = CSGTreeBuilder.Instance.CreateCSGTreeBuilderOptions();
        //    opt.Pline = false;
        //    opt.Holes = false;
        //    var csg = CSGTreeBuilder.Instance.GetGeometry(element, opt);
        //    if (csg.Length == 0) return result;


        //    foreach (var item in csg)
        //    {
        //        foreach (var csgItem in item.Items)
        //        {
        //            if (csgItem is CSGExtrusion)
        //            {
        //                var face = new List<double[]>();
        //                var csgExtrusion = csgItem as CSGExtrusion;

        //                foreach (var edge in csgExtrusion.Edges)
        //                {
        //                    var v = edge.Bottom;
        //                    face.Add(new[] { Math.Round(v.X, 4), Math.Round(v.Y, 4), 0.0, 0.0 });
        //                }
        //                if (face.Count > 0) result.Add(face);
        //            }
        //        }
        //    }
        //    return result;
        //}

        internal void GetMeshRepresentation(DbElement element, out List<List<List<double[]>>> faceSets, out List<double[]> matrixes)
        {
            faceSets = new List<List<List<double[]>>>();
            matrixes = new List<double[]>();
            var opt = CSGTreeBuilder.Instance.CreateCSGTreeBuilderOptions();
            opt.Pline = false;
            opt.Holes = false;
            var csg = CSGTreeBuilder.Instance.GetGeometry(element, opt);
            if (csg.Length == 0) return;

            foreach (var item in csg)
            {
                foreach (var csgItem in item.Items)
                {
                    if (csgItem is CSGFacetGroup)
                    {
                        var faceSet = new List<List<double[]>>();
                        var matrix = csgItem.Transform.AsArray12();
                        var facetGroup = csgItem as CSGFacetGroup;
                        foreach (var facet in facetGroup.Facets)
                        {
                            var face = new List<double[]>();
                            foreach (var loop in facet.Loops)
                            {
                                foreach (var vert in loop.Vertices)
                                {
                                    face.Add(new[] { vert.X, vert.Y, vert.Z });
                                }
                            }
                            faceSet.Add(face);
                        }
                        faceSets.Add(faceSet);
                        matrixes.Add(matrix);
                    }
                    else if (csgItem is CSGLine)
                    {
                        continue;
                    }
                    else
                    {
                        Console.WriteLine("wtf GetMeshRepresentation");
                    }
                }
            }
        }        

        internal List<PdmsGeometry> GetElecCSGRepresentation(DbElement element)
        {
            var result = new Dictionary<int, PdmsGeometry>();
            var opt = CSGTreeBuilder.Instance.CreateCSGTreeBuilderOptions();
            opt.Pline = false;
            opt.Holes = true;
            var csg = CSGTreeBuilder.Instance.GetGeometry(element, opt);
            if (csg.Length == 0) return new List<PdmsGeometry>();
            var type = element.GetElementType();
            foreach (var item in csg)
            {
                if (item.Element.GetElementType() == DbElementTypeInstance.POINTR || item.Element.GetElementType() == DbElementTypeInstance.RNODE) continue;
                foreach (var csgItem in item.Items)
                {
                    if (csgItem is CSGLine) continue;
                    
                    if (csgItem is CSGCylinder)
                    {
                        var geom = new PdmsCylinder(csgItem as CSGCylinder);
                        if (result.ContainsKey(geom.Hash))
                        {
                            result[geom.Hash].Translucency = Math.Min(result[geom.Hash].Translucency, geom.Translucency);
                            continue;
                        }
                        result.Add(geom.Hash, geom);
                    }
                    else if (csgItem is CSGCTorus)
                    {
                        var geom = new PdmsCircTorus(csgItem as CSGCTorus);
                        if (result.ContainsKey(geom.Hash))
                        {
                            result[geom.Hash].Translucency = Math.Min(result[geom.Hash].Translucency, geom.Translucency);
                            continue;
                        }
                        result.Add(geom.Hash, geom);
                    }
                    else if (csgItem is CSGRTorus)
                    {
                        var geom = new PdmsRectTorus(csgItem as CSGRTorus);
                        if (result.ContainsKey(geom.Hash))
                        {
                            result[geom.Hash].Translucency = Math.Min(result[geom.Hash].Translucency, geom.Translucency);
                            continue;
                        }
                        result.Add(geom.Hash, geom);
                    }
                    else if (csgItem is CSGSnout)
                    {
                        var snout = csgItem as CSGSnout;
                        PdmsGeometry geom = null;
                        if (snout.BottomRadius == snout.TopRadius && snout.BottomXShear == 0.0 &&
                            snout.BottomYShear == 0.0 && snout.TopXShear == 0.0 && snout.TopYShear == 0.0 &&
                            snout.XAxisDisplacement == 0.0 && snout.YAxisDisplacement == 0.0)
                        {
                            geom = new PdmsCylinder(snout);
                        }
                        else if (snout.BottomRadius != snout.TopRadius && snout.BottomXShear == 0.0 &&
                                 snout.BottomYShear == 0.0 && snout.TopXShear == 0.0 && snout.TopYShear == 0.0 &&
                                 snout.XAxisDisplacement == 0.0 && snout.YAxisDisplacement == 0.0)
                        {
                            geom = new PdmsCone(snout);
                        }
                        else if ((snout.BottomRadius == snout.TopRadius) && (snout.BottomXShear != 0.0 || snout.BottomYShear != 0.0 || snout.TopXShear != 0.0 || snout.TopYShear != 0.0))
                        {
                            geom = new PdmsSlcy(snout);
                        }
                        else if (snout.XAxisDisplacement != 0.0 || snout.YAxisDisplacement != 0.0)
                        {
                            geom = new PdmsSnout(snout);
                        }
                        else
                        {
                            Console.WriteLine("wtf GetElecCSGRepresentation");
                        }
                        if (result.ContainsKey(geom.Hash))
                        {
                            result[geom.Hash].Translucency = Math.Min(result[geom.Hash].Translucency, geom.Translucency);
                            continue;
                        }
                        result.Add(geom.Hash, geom);
                    }
                    else if (csgItem is CSGFacetGroup)
                    {
                        var geom = new PdmsFacetGroup(csgItem as CSGFacetGroup);
                        geom.Translucency = type == DbElementTypeInstance.CWBRAN &&
                                            item.Element.GetElementType() == DbElementTypeInstance.CWBRAN
                            ? 75
                            : geom.Translucency;
                        if (result.ContainsKey(geom.Hash))
                        {
                            result[geom.Hash].Translucency = Math.Min(result[geom.Hash].Translucency, geom.Translucency);
                            continue;
                        }
                        result.Add(geom.Hash, geom);
                    }
                    else if (csgItem is CSGBox)
                    {
                        var geom = new PdmsBox(csgItem as CSGBox);
                        if (result.ContainsKey(geom.Hash))
                        {
                            result[geom.Hash].Translucency = Math.Min(result[geom.Hash].Translucency, geom.Translucency);
                            continue;
                        }
                        result.Add(geom.Hash, geom);
                    }
                    else if (csgItem is CSGPyramid)
                    {
                        var geom = new PdmsPyramid(csgItem as CSGPyramid);
                        if (result.ContainsKey(geom.Hash))
                        {
                            result[geom.Hash].Translucency = Math.Min(result[geom.Hash].Translucency, geom.Translucency);
                            continue;
                        }
                        result.Add(geom.Hash, geom);
                    }
                    else if (csgItem is CSGSphere)
                    {
                        var geom = new PdmsSphere(csgItem as CSGSphere);
                        if (result.ContainsKey(geom.Hash))
                        {
                            result[geom.Hash].Translucency = Math.Min(result[geom.Hash].Translucency, geom.Translucency);
                            continue;
                        }
                        result.Add(geom.Hash, geom);
                    }
                    else if (csgItem is CSGDish)
                    {
                        var dish = csgItem as CSGDish;
                        PdmsGeometry geom;
                        if (dish.Attribute == 0.0)
                        {
                            geom = new PdmsSphericalDish(dish);
                        }
                        else
                        {
                            geom = new PdmsEllipticalDish(dish);
                        }

                        if (result.ContainsKey(geom.Hash))
                        {
                            result[geom.Hash].Translucency = Math.Min(result[geom.Hash].Translucency, geom.Translucency);
                            continue;
                        }
                        result.Add(geom.Hash, geom);
                    }
                    else
                    {
                        Console.WriteLine(csgItem.Type);
                    }
                }
            }
            return result.Values.ToList();
        }

        internal double EvaluateDouble(DbElement root, string expression)
        {
            var res = 0.0;
            if (expression == "unset") return res;
            expression = expression.Replace("DDHEIGHT", "HEIGHT").Replace("DESIGN PARAM","DESPARAM");

            if (expression.Contains(ExportIfc.Att_InsuThickness.Name))
            {
                var insuThickness = root.GetDouble(ExportIfc.Att_InsuThickness);
                if (insuThickness < 0) return 0;
                expression = expression.Replace(ExportIfc.Att_InsuThickness.Name, insuThickness.ToString());
                var parts = expression.Split('+');
                foreach (var part in parts)
                {
                    var exprPart = part;
                    if (part.StartsWith("("))
                        exprPart = part + ")";
                    else if (part.EndsWith(")"))
                        exprPart = "(" + part;
                    var value = 0.0;
                    if (!root.EvaluateValidDouble(ExportIfc.GetOrAddExpression(exprPart), DbAttributeUnit.NONE, ref value))
                    {
                        Console.WriteLine("Can't evaluate \"{0}\" at {1}", expression, root.GetAsString(DbAttributeInstance.FLNM));
                    }
                    res += value;
                }
            }
            else if (expression.Contains("SUM "))
            {
                var parts = expression.Split(new [] {' '}, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 5)
                {
                    Console.WriteLine("Check expression {0} on {1}",expression,root.GetAsString(DbAttributeInstance.FLNM));
                }
                var value = 0.0;
                var exp = string.Format("{0} {1}", parts[1], parts[2]);
                if (!root.EvaluateValidDouble(ExportIfc.GetOrAddExpression(exp), DbAttributeUnit.NONE, ref value) && exp != "IPARAM 1")
                {
                    Console.WriteLine("Can't evaluate \"{0}\" at {1}", exp, root.GetAsString(DbAttributeInstance.FLNM));
                }
                res += value;
                value = 0.0;
                exp = string.Format("{0} {1}", parts[3], parts[4]);
                if (!root.EvaluateValidDouble(ExportIfc.GetOrAddExpression(exp), DbAttributeUnit.NONE, ref value) && exp!="IPARAM 1")
                {
                    Console.WriteLine("Can't evaluate \"{0}\" at {1}", exp, root.GetAsString(DbAttributeInstance.FLNM));
                }
                res += value;
            }
            else
            {
                if (!root.EvaluateValidDouble(ExportIfc.GetOrAddExpression(expression), DbAttributeUnit.NONE, ref res))
                {
                    Console.WriteLine("Can't evaluate \"{0}\" at {1}", expression,
                        root.GetAsString(DbAttributeInstance.FLNM));
                }
            }
            return res;
        }

        internal void EvaluatePointDirPos(DbElement root, string expression, out Direction direction, out Position position)
        {
            expression = expression.Trim();
            position = Position.Create();
            var opposite = false;
            if (expression.StartsWith("-"))
            {
                opposite = true;
                expression = expression.Substring(1);
            }
            if (expression.Contains("P"))
            {
                expression = expression.Substring(1);
                direction = root.GetDirection(DbAttributeInstance.PDIR, int.Parse(expression), root);
                position = root.GetPosition(DbAttributeInstance.PPOS, int.Parse(expression), root);
            }
            else
            {
                direction = root.EvaluateDirection(ExportIfc.GetOrAddExpression(expression));
            }

            if (opposite)
                direction = direction.Opposite();
        }
    }

    internal class PdmsPyramid : PdmsGeometry
    {
        internal double Xbottom;
        internal double Ybottom;
        internal double Xtop;
        internal double Ytop;
        internal double Xoffset;
        internal double Yoffset;
        internal double Height;

        internal PdmsPyramid(DbElement item)
        {
            Xbottom = item.GetDouble(DbAttributeInstance.XBOT);
            Ybottom = item.GetDouble(DbAttributeInstance.YBOT);
            Xtop = item.GetDouble(DbAttributeInstance.XTOP);
            Ytop = item.GetDouble(DbAttributeInstance.YTOP);
            Xoffset = item.GetDouble(DbAttributeInstance.XOFF);
            Yoffset = item.GetDouble(DbAttributeInstance.YOFF);
            Height = item.GetDouble(DbAttributeInstance.HEIG);
            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal PdmsPyramid(CSGPyramid csg)
        {
            Xbottom = csg.XBottom;
            Ybottom = csg.YBottom;
            Xtop = csg.XTop;
            Ytop = csg.YTop;
            Xoffset = csg.XOffset;
            Yoffset = csg.YOffset;
            Height = csg.Height;
            Translucency = csg.Translucency;
            Matrix = csg.Transform.AsArray12();
            Hash = GetHash();
        }

        internal PdmsPyramid(DbElement root, DbElement gmItem)
        {
            Xbottom = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PBBT));
            Ybottom = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PCBT));
            Xtop = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PBTP));
            Ytop = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PCTP));

            Xoffset = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PBOF));
            Yoffset = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PCOF));

            var ptdi = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PTDI));
            var pbdi = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PBDI));

            if (ptdi < pbdi)
            {
                var tp = ptdi;
                ptdi = pbdi;
                pbdi = tp;

                tp = Xbottom;
                Xbottom = Xtop;
                Xtop = tp;

                tp = Ybottom;
                Ybottom = Ytop;
                Ytop = tp;

                Xoffset = -Xoffset;
                Yoffset = -Yoffset;
            }
            Height = ptdi - pbdi;

            Direction directionX, directionY, directionZ;
            Position position;
            Orientation orientation;

            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PBAX), out directionX, out position);
            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PCAX), out directionY, out position);
            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PAAX), out directionZ, out position);

            PdmsMessage error;
            if (!Orientation.Parse("X IS " + directionX + " AND Z IS " + directionZ, out orientation, out error) || orientation.ZDir() != directionZ)
            {
                orientation = Orientation.Create("Z IS " + directionZ);
                directionX = orientation.YDir();
                orientation = Orientation.Create("X IS " + directionX + " AND Z IS " + directionZ);
            }

            directionX = orientation.XDir();
            directionY = orientation.YDir();
            directionZ = orientation.ZDir();
            position.MoveBy(directionZ, pbdi + Height / 2.0);
            position.MoveBy(directionX, Xoffset / 2.0);
            position.MoveBy(directionY, Yoffset / 2.0);

            Matrix = GetMatrix(root, orientation, position);

            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name+" ");
            for (var i = 0; i < Matrix.Length; i++)
                sb.Append(string.Format("{0:0.0000} ", Matrix[i]));
            sb.Append(string.Format("{0} {1} {2} {3} {4} {5} {6} {7} {8}",Color, Translucency, Xbottom, Ybottom, Xtop, Ytop, Xoffset, Yoffset,
                Height));
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append(string.Format(" {0} {1} {2} {3} {4} {5} {6} {7}", 0, Xbottom, Ybottom, Xtop, Ytop, Xoffset,
                Yoffset,
                Height));
            return sb.ToString().GetHashCode();
        }
    }

    internal class PdmsBox : PdmsGeometry
    {
        internal double Xlength;
        internal double Ylength;
        internal double Zlength;

        internal PdmsBox(DbElement item)
        {
            Xlength = item.GetDouble(DbAttributeInstance.XLEN);
            Ylength = item.GetDouble(DbAttributeInstance.YLEN);
            Zlength = item.GetDouble(DbAttributeInstance.ZLEN);
            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal PdmsBox(CSGBox csg)
        {
            Xlength = csg.XLength;
            Ylength = csg.YLength;
            Zlength = csg.ZLength;
            Matrix = csg.Transform.AsArray12();
            Translucency = csg.Translucency;
            Hash = GetHash();
        }

        internal PdmsBox(DbElement root, DbElement gmItem)
        {
            if (gmItem.GetElementType() == DbElementTypeInstance.SRECTANGLE)
            {
                var poss = root.GetPosition(DbAttributeInstance.POSS);
                var pose = root.GetPosition(DbAttributeInstance.POSE);
                var jusl = root.GetAsString(DbAttributeInstance.JUSL).Replace("unset", "NA");

                Xlength = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PXLE));
                Ylength = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PYLE));
                Zlength = poss.Distance(pose);

                var px = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PX));
                var py = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PY));

                Direction directionY;
                Position positionY;
                var positionPlin = root.EvaluatePosition(ExportIfc.GetOrAddExpression("PPLIN " + jusl + " OFFS WRT CE"));

                EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PLAX), out directionY, out positionY);
                
                var orientation = Orientation.Create("Y IS " + directionY);
                var position = Position.Create(px, py, Zlength/2.0);
                position.MoveBy(orientation.XDir(), -positionPlin.X);
                position.MoveBy(directionY, -positionPlin.Y);

                Matrix = GetMatrix(root, orientation, position);
            }
            else
            {
                Xlength = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PXLE));
                Ylength = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PYLE));
                Zlength = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PZLE));

                var px = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PX));
                var py = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PY));
                var pz = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PZ));

                var orientation = Orientation.Create("Z IS UP");
                var position = Position.Create(px, py, pz);
                Matrix = GetMatrix(root, orientation, position);
            }

            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name + " ");
            for (var i = 0; i < Matrix.Length; i++)
                sb.Append(string.Format("{0:0.0000} ", Matrix[i]));
            sb.Append(string.Format("{0} {1} {2} {3} {4}",Color, Translucency, Xlength, Ylength, Zlength));
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append(string.Format(" {0} {1} {2} {3}", 0, Xlength, Ylength, Zlength));
            return sb.ToString().GetHashCode();
        }
    }

    internal class PdmsRectTorus : PdmsGeometry
    {
        internal double Rinside;
        internal double Routside;
        internal double Height;
        internal double Angle;

        internal PdmsRectTorus(DbElement item)
        {
            Rinside = item.GetDouble(DbAttributeInstance.RINS);
            Routside = item.GetDouble(DbAttributeInstance.ROUT);
            Height = item.GetDouble(DbAttributeInstance.HEIG);
            Angle = (Math.PI / 180) * item.GetDouble(DbAttributeInstance.ANGL);
            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal PdmsRectTorus(CSGRTorus csg)
        {
            Rinside = csg.InsideRadius;
            Routside = csg.OutsideRadius;
            Height = csg.Height;
            Angle = csg.Angle;
            Matrix = csg.Transform.AsArray12();
            Hash = GetHash();
        }

        internal PdmsRectTorus(DbElement root, DbElement gmItem)
        {
            Height = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PHEI));
            var radius = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIA)) / 2.0;

            Direction directionX, directionZ;
            Position position, positionX, positionZ;
            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PBAX), out directionX, out positionX);
            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PAAX), out directionZ, out positionZ);

            var positionZ2 = Position.Create(positionZ.X, positionZ.Y, positionZ.Z);
            positionZ2.MoveBy(directionZ, 1.0);
            var pposB2 = Position.Create(positionX.X, positionX.Y, positionX.Z);
            pposB2.MoveBy(directionX, 1.0);

            var line1 = Line.Create(positionZ, positionZ2);
            var line2 = Line.Create(positionX, pposB2);
            var pposI = line1.Intersection(line2);

            double rcentre;
            if (pposI == null)
            {
                position = positionZ.MidPoint(positionX);
                rcentre = position.Distance(positionZ);
                Angle = Math.PI;
            }
            else
            {
                var aSide = pposI.Distance(positionZ);
                var alfa = (Math.PI / 180) * directionZ.Angle(directionX) / 2.0;
                rcentre = aSide * Math.Tan(alfa);
                var dist = aSide / Math.Cos(alfa);
                var dir = directionX.Midway(directionZ);
                pposI.MoveBy(dir, dist);
                Angle = (Math.PI / 180) * pposI.Angle(positionZ, positionX);
                position = pposI;
            }

            Rinside = rcentre - radius;
            Routside = rcentre + radius;

            var orientation = Orientation.Create("X IS " + Direction.Create(position, positionX) + " AND Y IS " + directionX.Opposite());
            Matrix = GetMatrix(root, orientation, position);

            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name + " ");
            for (var i = 0; i < Matrix.Length; i++)
                sb.Append(string.Format("{0:0.0000} ", Matrix[i]));
            sb.Append(string.Format("{0} {1} {2} {3} {4} {5}", Color, Translucency, Rinside, Routside, Height, Angle));
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append(string.Format(" {0} {1} {2} {3} {4}", 0, Rinside, Routside, Height, Angle));
            return sb.ToString().GetHashCode();
        }
    }

    internal class PdmsCircTorus : PdmsGeometry
    {
        internal double Rcentre;
        internal double Radius;
        internal double Angle;

        internal PdmsCircTorus(DbElement item)
        {
            if (item.GetElementType() == DbElementTypeInstance.BEND)
            {
                Rcentre = item.GetDouble(DbAttributeInstance.RADI);
                Radius = item.GetDouble(DbAttributeInstance.AOD) / 2.0;
                var directionZ = item.GetDirection(DbAttributeInstance.PDIR, 1, item);
                var positionZ = item.GetPosition(DbAttributeInstance.PPOS, 1, item);
                var directionX = item.GetDirection(DbAttributeInstance.PDIR, 2, item);
                var positionX = item.GetPosition(DbAttributeInstance.PPOS, 2, item);

                var positionZ2 = Position.Create(positionZ.X, positionZ.Y, positionZ.Z);
                positionZ2.MoveBy(directionZ, 1.0);
                var positionX2 = Position.Create(positionX.X, positionX.Y, positionX.Z);
                positionX2.MoveBy(directionX, 1.0);

                var line1 = Line.Create(positionZ, positionZ2);
                var line2 = Line.Create(positionX, positionX2);
                var position = line1.Intersection(line2);

                var aSide = position.Distance(positionZ);
                var alfa = (Math.PI / 180) * directionZ.Angle(directionX) / 2.0;

                var dist = aSide / Math.Cos(alfa);
                var dir = directionX.Midway(directionZ);
                position.MoveBy(dir, dist);

                var orientation = Orientation.Create("X IS " + Direction.Create(position, positionX) + " AND Y IS " + directionX.Opposite());

                Matrix = GetMatrix(item, orientation, position);
            }
            else
            {
                var rins = item.GetDouble(DbAttributeInstance.RINS);
                var rout = item.GetDouble(DbAttributeInstance.ROUT);

                Radius = (rout - rins) / 2.0;
                Rcentre = rins + Radius;

                Matrix = GetMatrix(item);
            }
            Angle = (Math.PI / 180) * item.GetDouble(DbAttributeInstance.ANGL);
            Hash = GetHash();
        }

        internal PdmsCircTorus(CSGCTorus csg)
        {
            Radius = csg.SectionRadius;
            Rcentre = csg.ArcRadius;
            Angle = csg.Angle;
            Matrix = csg.Transform.AsArray12();
            Translucency = csg.Translucency;
            Hash = GetHash();
        }

        internal PdmsCircTorus(DbElement root, DbElement gmItem)
        {
            Radius = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIA)) / 2.0;

            Direction directionX, directionZ;
            Position position, positionX, positionZ;
            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PBAX), out directionX, out positionX);
            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PAAX), out directionZ, out positionZ);

            var positionZ2 = Position.Create(positionZ.X, positionZ.Y, positionZ.Z);
            positionZ2.MoveBy(directionZ, 1.0);
            var positionX2 = Position.Create(positionX.X, positionX.Y, positionX.Z);
            positionX2.MoveBy(directionX, 1.0);

            var line1 = Line.Create(positionZ, positionZ2);
            var line2 = Line.Create(positionX, positionX2);
            var pposI = line1.Intersection(line2);
            if (pposI == null)
            {
                position = positionZ.MidPoint(positionX);
                Rcentre = position.Distance(positionZ);
                Angle = Math.PI;
            }
            else
            {
                var aSide = pposI.Distance(positionZ);
                var alfa = (Math.PI / 180) * directionZ.Angle(directionX) / 2.0;
                Rcentre = aSide * Math.Tan(alfa);
                var dist = aSide / Math.Cos(alfa);
                var dir = directionX.Midway(directionZ);
                pposI.MoveBy(dir, dist);
                Angle = (Math.PI / 180) * pposI.Angle(positionZ, positionX);
                position = pposI;
            }

            //var orientation =Orientation.Create("X IS "+Direction.Create(ppos,pposA)+" AND Y IS " +directionA);
            var orientation = Orientation.Create("X IS " + Direction.Create(position, positionX) + " AND Y IS " + directionX.Opposite());

            Matrix = GetMatrix(root, orientation, position);

            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name + " ");
            for (var i = 0; i < Matrix.Length; i++)
                sb.Append(string.Format("{0:0.0000} ", Matrix[i]));
            sb.Append(string.Format("{0} {1} {2} {3} {4}", Color, Translucency, Rcentre, Radius, Angle));
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append(string.Format(" {0} {1} {2} {3}", 0, Rcentre, Radius, Angle));
            return sb.ToString().GetHashCode();
        }
    }

    internal class PdmsEllipticalDish : PdmsGeometry
    {
        internal double Radius;
        internal double Height;

        internal PdmsEllipticalDish(DbElement item)
        {
            Radius = item.GetDouble(DbAttributeInstance.DIAM) / 2.0;
            Height = item.GetDouble(DbAttributeInstance.HEIG);
            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal PdmsEllipticalDish(CSGDish csg)
        {
            Radius = csg.Radius;
            Height = csg.Height;
            Translucency = csg.Translucency;
            Matrix = csg.Transform.AsArray12();
            Hash = GetHash();
        }

        internal PdmsEllipticalDish(DbElement root, DbElement gmItem)
        {
            Radius = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIA)) / 2.0;
            Height = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PHEI));

            var pdis = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIS));

            Direction directionZ;
            Position positionZ;
            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PAXI), out directionZ, out positionZ);

            var position = Position.Create();
            position.MoveBy(directionZ, pdis);

            var orientation = Orientation.Create("Z IS " + directionZ);
            Matrix = GetMatrix(root, orientation, position);
            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name + " ");
            for (var i = 0; i < Matrix.Length; i++)
                sb.Append(string.Format("{0:0.0000} ", Matrix[i]));
            sb.Append(string.Format("{0} {1} {2} {3}", Color, Translucency, Radius, Height));
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append(string.Format(" {0} {1} {2}", 0, Radius, Height));
            return sb.ToString().GetHashCode();
        }
    }

    internal class PdmsSphericalDish : PdmsGeometry
    {
        internal double Radius;
        internal double Height;

        internal PdmsSphericalDish(DbElement item)
        {
            Radius = item.GetDouble(DbAttributeInstance.DIAM) / 2.0;
            Height = item.GetDouble(DbAttributeInstance.HEIG);
            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal PdmsSphericalDish(CSGDish csg)
        {
            Radius = csg.Radius;
            Height = csg.Height;
            Translucency = csg.Translucency;
            Matrix = csg.Transform.AsArray12();
            Hash = GetHash();
        }

        internal PdmsSphericalDish(DbElement root, DbElement gmItem)
        {
            Radius = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIA)) / 2.0;
            Height = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PHEI));

            var pdis = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIS));

            Direction directionZ;
            Position positionZ;
            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PAXI), out directionZ, out positionZ);

            var position = Position.Create();
            position.MoveBy(directionZ, pdis);

            var orientation = Orientation.Create("Z IS " + directionZ);
            Matrix = GetMatrix(root, orientation, position);
            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name + " ");
            for (var i = 0; i < Matrix.Length; i++)
                sb.Append(string.Format("{0:0.0000} ", Matrix[i]));
            sb.Append(string.Format("{0} {1} {2} {3}", Color, Translucency, Radius, Height));
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append(string.Format(" {0} {1} {2}", 0, Radius, Height));
            return sb.ToString().GetHashCode();
        }
    }

    internal class PdmsSnout : PdmsGeometry
    {
        internal double Rbottom;
        internal double Rtop;
        internal double Height;
        internal double Xoffset;
        internal double Yoffset;

        internal PdmsSnout(DbElement item)
        {
            Rbottom = item.GetDouble(DbAttributeInstance.DBOT) / 2.0;
            Rtop = item.GetDouble(DbAttributeInstance.DTOP) / 2.0;
            Height = item.GetDouble(DbAttributeInstance.HEIG);
            Xoffset = item.GetDouble(DbAttributeInstance.XOFF);
            Yoffset = item.GetDouble(DbAttributeInstance.YOFF);
            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal PdmsSnout(CSGSnout csg)
        {
            Rbottom = csg.BottomRadius;
            Rtop = csg.TopRadius;
            Height = csg.Height;
            Xoffset = csg.XAxisDisplacement;
            Yoffset = csg.YAxisDisplacement;
            Matrix = csg.Transform.AsArray12();
            Translucency = csg.Translucency;
            Hash = GetHash();
        }

        internal PdmsSnout(DbElement root, DbElement gmItem)
        {
            var ptdi = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PTDI));
            var pbdi = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PBDI));
            Rbottom = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PBDM)) / 2.0;
            Rtop = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PTDM)) / 2.0;
            Xoffset = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.POFF));

            if (ptdi < pbdi)
            {
                var tp = ptdi;
                ptdi = pbdi;
                pbdi = tp;
                tp = Rbottom;
                Rbottom = Rtop;
                Rtop = tp;
                Xoffset = -Xoffset;
            }
            Height = ptdi - pbdi;

            Direction directionX, directionZ;
            Position positionX, positionZ;
            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PBAX), out directionX, out positionX);
            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PAAX), out directionZ, out positionZ);

            var position = positionZ;
            position.MoveBy(directionZ, pbdi + Height / 2.0);
            position.MoveBy(directionX, Xoffset / 2.0);

            Orientation orientation;
            PdmsMessage error;
            if (!Orientation.Parse("X IS " + directionX + " AND Z IS " + directionZ, out orientation, out error) || orientation.ZDir() != directionZ)
            {
                orientation = Orientation.Create("Z IS " + directionZ);
                directionX = orientation.YDir();
                orientation = Orientation.Create("X IS " + directionX + " AND Z IS " + directionZ);
            }

            Matrix = GetMatrix(root, orientation, position);

            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name + " ");
            for (var i = 0; i < Matrix.Length; i++)
                sb.Append(string.Format("{0:0.0000} ", Matrix[i]));
            sb.Append(string.Format("{0} {1} {2} {3} {4} {5} {6}", Color, Translucency, Rbottom, Rtop, Height, Xoffset, Yoffset));
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append(string.Format(" {0} {1} {2} {3} {4} {5}", 0, Rbottom, Rtop, Height, Xoffset, Yoffset));
            return sb.ToString().GetHashCode();
        }
    }

    internal class PdmsSlcy : PdmsGeometry
    {
        internal double Radius;
        internal double Height;
        internal double Angle1;
        internal double Angle2;
        internal double Angle3;
        internal double Angle4;

        internal PdmsSlcy(DbElement item)
        {
            Radius = item.GetDouble(DbAttributeInstance.DIAM) / 2.0;
            Height = item.GetDouble(DbAttributeInstance.HEIG);

            Angle1 = (Math.PI / 180) * item.GetDouble(DbAttributeInstance.XBSH);
            Angle2 = (Math.PI / 180) * item.GetDouble(DbAttributeInstance.YBSH);
            Angle3 = (Math.PI / 180) * item.GetDouble(DbAttributeInstance.XTSH);
            Angle4 = (Math.PI / 180) * item.GetDouble(DbAttributeInstance.YTSH);
            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal PdmsSlcy(CSGSnout csg)
        {
            Radius = csg.BottomRadius;
            Height = csg.Height;

            Angle1 = csg.BottomXShear;
            Angle2 = csg.BottomYShear;
            Angle3 = csg.TopXShear;
            Angle4 = csg.TopYShear;
            Matrix = csg.Transform.AsArray12();
            Translucency = csg.Translucency;
            Hash = GetHash();
        }

        internal PdmsSlcy(DbElement root, DbElement gmItem)
        {
            Height = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PHEI));
            var pdist = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIS));
            Radius = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIA)) / 2.0;

            Angle1 = (Math.PI / 180) * EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PXBS));
            Angle2 = (Math.PI / 180) * EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PYBS));
            Angle3 = (Math.PI / 180) * EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PXTS));
            Angle4 = (Math.PI / 180) * EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PYTS));

            Direction directionZ;
            Position positionZ;
            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PAXI), out directionZ, out positionZ);

            var position = positionZ;
            position.MoveBy(directionZ, pdist + Height / 2.0);

            var zAngle = D3Vector.Create(directionZ.East, directionZ.North, directionZ.Up).Angle(D3Vector.D3UP);
            if (zAngle > 0)
            {
                var a = Angle1;
                Angle1 = zAngle >= Math.PI / 2 ? -Angle2 : Angle2;
                Angle2 = zAngle >= Math.PI / 2 ? -a : a;
                a = Angle3;
                Angle3 = zAngle >= Math.PI / 2 ? -Angle4 : Angle4;
                Angle4 = zAngle >= Math.PI / 2 ? -a : a;
            }

            var orientation = Orientation.Create("Z IS " + directionZ);
            Matrix = GetMatrix(root, orientation, position);

            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name + " ");
            for (var i = 0; i < Matrix.Length; i++)
                sb.Append(string.Format("{0:0.0000} ", Matrix[i]));
            sb.Append(string.Format("{0} {1} {2} {3} {4} {5} {6} {7}", Color, Translucency, Radius, Height, Angle1, Angle2,
                Angle3, Angle4));
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append(string.Format(" {0} {1} {2} {3} {4} {5} {6}", 0, Radius, Height, Angle1, Angle2,
                Angle3, Angle4));
            return sb.ToString().GetHashCode();
        }
    }

    internal class PdmsCone : PdmsGeometry
    {
        internal double Rbottom;
        internal double Rtop;
        internal double Height;

        internal PdmsCone(DbElement item)
        {
            Rbottom = item.GetDouble(DbAttributeInstance.DBOT) / 2.0;
            Rtop = item.GetDouble(DbAttributeInstance.DTOP) / 2.0;
            Height = item.GetDouble(DbAttributeInstance.HEIG);
            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal PdmsCone(CSGSnout csg)
        {
            Rbottom = csg.BottomRadius;
            Rtop = csg.TopRadius;
            Height = csg.Height;
            Matrix = csg.Transform.AsArray12();
            Translucency = csg.Translucency;
            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name + " ");
            for (var i = 0; i < Matrix.Length; i++)
                sb.Append(string.Format("{0:0.0000} ", Matrix[i]));
            sb.Append(string.Format("{0} {1} {2} {3} {4}", Color, Translucency, Rbottom, Rtop, Height));
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append(string.Format(" {0} {1} {2} {3}", 0, Rbottom, Rtop, Height));
            return sb.ToString().GetHashCode();
        }
    }

    internal class PdmsCylinder : PdmsGeometry
    {
        internal double Radius;
        internal double Height;

        internal PdmsCylinder(DbElement item)
        {
            Radius = item.GetDouble(DbAttributeInstance.DIAM) / 2.0;
            Height = item.GetDouble(DbAttributeInstance.HEIG);
            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal PdmsCylinder(double radius, double height, double[] matrix)
        {
            Radius = radius;
            Height = height;
            Matrix = matrix;
            Hash = GetHash();
        }

        internal PdmsCylinder(DbElement root, DbElement gmItem)
        {
            if (gmItem.GetElementType() == DbElementTypeInstance.SCYLINDER || gmItem.GetElementType() == DbElementTypeInstance.NSCYLINDER)
            {
                Height =EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PHEI));
                var pdist = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIS));
                Radius = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIA)) / 2.0;

                if (Height < 0)
                {
                    Height = -Height;
                    pdist = pdist-Height;
                }
                Direction directionZ;
                Position positionZ;
                EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PAXI), out directionZ, out positionZ);

                var position = positionZ;
                position.MoveBy(directionZ, pdist + Height / 2.0);

                var orientation = Orientation.Create("Z IS " + directionZ);
                Matrix = GetMatrix(root, orientation, position);

            }
            else if (gmItem.GetElementType() == DbElementTypeInstance.LCYLINDER || gmItem.GetElementType() == DbElementTypeInstance.NLCYLINDER)
            {
                var ptdi = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PTDI));
                var pbdi = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PBDI));

                if (ptdi < pbdi)
                {
                    var tp = ptdi;
                    ptdi = pbdi;
                    pbdi = tp;
                }
                Height = ptdi - pbdi;

                Radius = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIA)) / 2.0;
                Direction directionZ;
                Position positionZ;
                EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PAXI), out directionZ, out positionZ);

                var position = positionZ;
                position.MoveBy(directionZ, pbdi + Height / 2.0);

                var orientation = Orientation.Create("Z IS " + directionZ);
                Matrix = GetMatrix(root, orientation, position);
            }
            else if (gmItem.GetElementType() == DbElementTypeInstance.TUBE)
            {
                if (root.GetElementType() != DbElementTypeInstance.TUBING)
                {
                    Console.WriteLine();
                }
                Height = root.GetDouble(DbAttributeInstance.ITLE);
                Radius = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIA))/2.0;

                var orientation = Orientation.Create("Z IS " + root.GetOrientation(DbAttributeInstance.WORI).YDir());
                var position = root.GetPosition(DbAttributeInstance.WORPOS);
                var xdir = orientation.XDir();
                var ydir = orientation.YDir();
                var zdir = orientation.ZDir();
                Matrix = new[]
                {
                    xdir.East, xdir.North, xdir.Up,
                    ydir.East, ydir.North, ydir.Up,
                    zdir.East, zdir.North, zdir.Up,
                    position.X, position.Y, position.Z
                };
            }

            Hash = GetHash();
        }

        internal PdmsCylinder(CSGCylinder csg)
        {
            Radius = csg.Radius;
            Height = csg.Height;
            Matrix = csg.Transform.AsArray12();
            Translucency = csg.Translucency;
            Hash = GetHash();
        }

        internal PdmsCylinder(CSGSnout csg)
        {
            Radius = csg.BottomRadius;
            Height = csg.Height;
            Matrix = csg.Transform.AsArray12();
            Translucency = csg.Translucency;
            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name + " ");
            for (var i = 0; i < Matrix.Length; i++)
                sb.Append(string.Format("{0:0.0000} ", Matrix[i]));
            sb.Append(string.Format("{0} {1} {2} {3}", Color, Translucency, Radius, Height));
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append(string.Format(" {0} {1} {2}", 0, Radius, Height));
            return sb.ToString().GetHashCode();
        }
    }

    internal class PdmsSphere : PdmsGeometry
    {
        internal double Radius;

        internal PdmsSphere(DbElement item)
        {
            Radius = item.GetDouble(DbAttributeInstance.DIAM) / 2.0;
            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal PdmsSphere(CSGSphere csg)
        {
            Radius = csg.Radius;
            Translucency = csg.Translucency;
            Matrix = csg.Transform.AsArray12();
            Hash = GetHash();
        }

        internal PdmsSphere(DbElement root, DbElement gmItem)
        {
            var pdist = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIS));
            Radius = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIA))/2.0;

            Direction directionZ;
            Position positionZ;
            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PAXI), out directionZ, out positionZ);

            if (root.GetElementType() == DbElementTypeInstance.TUBING)
            {
                directionZ = root.GetDirection(DbAttributeInstance.PDIR, 0, root);
            }
            var position = positionZ;
            position.MoveBy(directionZ, pdist);

            var orientation = Orientation.Create("Z IS " + directionZ);
            Matrix = GetMatrix(root, orientation, position);

            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name + " ");
            for (var i = 0; i < Matrix.Length; i++)
                sb.Append(string.Format("{0:0.0000} ", Matrix[i]));
            sb.Append(string.Format("{0} {1} {2}", Color, Translucency, Radius));
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append(string.Format(" {0} {1}", 0, Radius));
            return sb.ToString().GetHashCode();
        }
    }

    internal class PdmsRevolution : PdmsGeometry
    {
        internal double Angle;
        internal List<double[]> Profile;

        internal PdmsRevolution(DbElement item)
        {
            Angle = (Math.PI / 180) * item.GetDouble(DbAttributeInstance.ANGL);
            Profile = new List<double[]>();
            foreach (var loop in item.Members(DbElementTypeInstance.LOOP))
            {
                foreach (var vertex in loop.Members(DbElementTypeInstance.VERTEX))
                {
                    var pos = vertex.GetPosition(DbAttributeInstance.POS);
                    Profile.Add(new[] { pos.X, pos.Y, pos.Z, vertex.GetDouble(DbAttributeInstance.FRAD) });
                }
            }

            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal PdmsRevolution(List<double[]> profile, double angle, double[] matrix, List<PdmsGeometry> negatives)
        {
            Angle = angle;
            Profile = profile;
            Matrix = matrix;
            Negatives = negatives;
            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name + " ");
            for (var i = 0; i < Matrix.Length; i++)
                sb.Append(string.Format("{0:0.0000} ", Matrix[i]));
            sb.Append(string.Format("{0} {1} {2} ", Color, Translucency, Angle));
            foreach (var v in Profile)
            {
                sb.Append(string.Format("{0:0.####} {1:0.####} {2:0.####} {3:0.####} ", v[0], v[1], v[2], v[3]));
            }
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append(string.Format(" {0} {1}", 0, Angle));
            foreach (var v in Profile)
            {
                sb.Append(string.Format(" {0} {1} {2} {3}", v[0], v[1], v[2], v[3]));
            }
            return sb.ToString().GetHashCode();
        }
    }

    internal class PdmsExtrusion : PdmsGeometry
    {
        internal double Height;
        internal List<double[]> Profile;

        internal PdmsExtrusion(DbElement item)
        {
            if (item.GetElementType() == DbElementTypeInstance.EXTRUSION ||
                item.GetElementType() == DbElementTypeInstance.NXTRUSION)
            {
                Height = item.GetDouble(DbAttributeInstance.HEIG);

                Profile = new List<double[]>();
                var loops = item.Members(DbElementTypeInstance.LOOP);
                if (loops.Length > 0)
                {
                    var loop = loops.First();
                    foreach (var vertex in loop.Members(DbElementTypeInstance.VERTEX))
                    {
                        var pos = vertex.GetPosition(DbAttributeInstance.POS);
                        Profile.Add(new[] { pos.X, pos.Y, pos.Z, vertex.GetDouble(DbAttributeInstance.FRAD) });
                    }
                }
            }
            else if (item.GetElementType() == DbElementTypeInstance.FLOOR || item.GetElementType() == DbElementTypeInstance.GWALL || item.GetElementType() == DbElementTypeInstance.PANEL)
            {
                Profile = new List<double[]>();
                var ploops = item.Members(DbElementTypeInstance.PLOOP);
                if (ploops.Length > 0)
                {
                    var ploop = ploops.First();
                    Height = ploop.GetDouble(DbAttributeInstance.HEIG);
                    foreach (var pavert in ploop.Members(DbElementTypeInstance.PAVERT))
                    {
                        var pos = pavert.GetPosition(DbAttributeInstance.POS);
                        Profile.Add(new[] { pos.X, pos.Y, pos.Z, pavert.GetDouble(DbAttributeInstance.FRAD) });
                    }
                }
            }

            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal PdmsExtrusion(List<double[]> profile, double height, double[] matrix, List<PdmsGeometry> negatives)
        {
            Height = height;
            Profile = profile;
            Matrix = matrix;
            Negatives = negatives;
            Hash = GetHash();
        }

        internal PdmsExtrusion(DbElement root, DbElement gmItem)
        {
            Profile = new List<double[]>();
            if (gmItem.GetElementType() == DbElementTypeInstance.SPROFILE)
            {
                var poss = root.GetPosition(DbAttributeInstance.POSS);
                var pose = root.GetPosition(DbAttributeInstance.POSE);
                var jusl = root.GetAsString(DbAttributeInstance.JUSL).Replace("unset","NA");

                Height = poss.Distance(pose);

                Direction directionY;
                Position positionY;
                var positionPlin = root.EvaluatePosition(ExportIfc.GetOrAddExpression("PPLIN " + jusl + " OFFS WRT CE"));

                EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PLAX), out directionY, out positionY);
                foreach (var spve in gmItem.Members(DbElementTypeInstance.SPVERT))
                {
                    var spveX = EvaluateDouble(root, spve.GetAsString(DbAttributeInstance.PX));
                    var spveY = EvaluateDouble(root, spve.GetAsString(DbAttributeInstance.PY));
                    var spveRad = EvaluateDouble(root, spve.GetAsString(DbAttributeInstance.PRAD));
                    Profile.Add(new[] {spveX, spveY, 0.0, spveRad});
                }

                var orientation = Orientation.Create("Y IS " + directionY);
                var position = Position.Create();
                position.MoveBy(orientation.XDir(), -positionPlin.X);
                position.MoveBy(directionY, -positionPlin.Y);

                Matrix = GetMatrix(root, orientation, position);
            }
            else
            {
                Height = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PHEI));
                var px = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PX));
                var py = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PY));
                var pz = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PZ));

                Direction directionX, directionY, directionZ;
                Position position, positionY;
                Orientation orientation;

                EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PAAX), out directionX, out position);
                EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PBAX), out directionY, out positionY);

                PdmsMessage error;
                if (!Orientation.Parse("X IS " + directionX + " AND Y IS " + directionY, out orientation, out error) ||
                    orientation.XDir() != directionX)
                {
                    orientation = Orientation.Create("X IS " + directionX);
                }

                directionX = orientation.XDir();
                directionY = orientation.YDir();
                directionZ = orientation.ZDir();

                position.MoveBy(directionX, px);
                position.MoveBy(directionY, py);
                position.MoveBy(directionZ, pz);

                Matrix = GetMatrix(root, orientation, position);

                foreach (var sloo in gmItem.Members(DbElementTypeInstance.SLOOP))
                {
                    foreach (var sver in sloo.Members(DbElementTypeInstance.SVERTEX))
                    {
                        var sverX = EvaluateDouble(root, sver.GetAsString(DbAttributeInstance.PX));
                        var sverY = EvaluateDouble(root, sver.GetAsString(DbAttributeInstance.PY));
                        var sverRad = EvaluateDouble(root, sver.GetAsString(DbAttributeInstance.PRAD));
                        Profile.Add(new[] {sverX, sverY, 0.0, sverRad});
                    }
                }
            }
            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name + " ");
            for (var i = 0; i < Matrix.Length; i++)
                sb.Append(string.Format("{0:0.0000} ", Matrix[i]));
            sb.Append(string.Format("{0} {1} {2} ", Color, Translucency, Height));
            foreach (var v in Profile)
            {
                sb.Append(string.Format("{0:0.####} {1:0.####} {2:0.####} {3:0.####} ", v[0], v[1], v[2], v[3]));
            }
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append(string.Format(" {0} {1}", 0, Height));
            foreach (var v in Profile)
            {
                sb.Append(string.Format(" {0} {1} {2} {3}", v[0], v[1], v[2], v[3]));
            }
            return sb.ToString().GetHashCode();
        }
    }

    internal class PdmsFacetGroup : PdmsGeometry
    {
        internal List<List<double[]>> Faces;

        internal PdmsFacetGroup(List<List<double[]>> faces, double[] matrix)
        {
            Faces = faces;
            Matrix = matrix;
            Hash = GetHash();
        }

        internal PdmsFacetGroup(CSGFacetGroup csg)
        {
            Faces = new List<List<double[]>>();
            foreach (var facet in csg.Facets)
            {
                foreach (var loop in facet.Loops)
                {
                    var face = new List<double[]>();
                    foreach (var vert in loop.Vertices)
                    {
                        face.Add(new[] { vert.X, vert.Y, vert.Z });
                    }
                    Faces.Add(face);
                }
            }
            Matrix = csg.Transform.AsArray12();
            Translucency = csg.Translucency;
            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name + " ");
            for (var i = 0; i < Matrix.Length; i++)
                sb.Append(string.Format("{0:0.0000} ", Matrix[i]));
            sb.Append(string.Format("{0} {1};", Color, Translucency));

            foreach (var face in Faces)
            {
                foreach (var arr in face)
                {
                    sb.Append(string.Format(" {0:0.####} {1:0.####} {2:0.####}", arr[0], arr[1], arr[2]));
                }
                sb.Append(";");
            }

            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append(string.Format(" {0}", 0));

            foreach (var face in Faces)
            {
                sb.Append(" ;");
                foreach (var arr in face)
                {
                    sb.Append(string.Format(" {0} {1} {2}", arr[0], arr[1], arr[2]));
                }
            }
            return sb.ToString().GetHashCode();
        }
    }
}