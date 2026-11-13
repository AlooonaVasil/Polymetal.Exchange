//using Aveva.Pdms.Database;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using Aveva.Pdms.CSG;
//using Aveva.Pdms.Maths.Geometry;

//namespace Polymetal.Pdms.Ifc
//{
//    class GeometryCreator
//    {
//        private const int REPRE_LEVEL = 6;
//        private List<string[]> ColorRules;
//        internal StringBuilder TexGeometry;

//        internal GeometryCreator(DbElement root)
//        {
//            ColorRules = new List<string[]>(); 
//            var geometries = new List<PdmsGeometry>();
//            if (root.GetElementType() == DbElementTypeInstance.EQUIPMENT)
//            {
//                var geomList = GetEquiGeometry(root);
//                foreach (var geom in geomList)
//                {
//                    if (geom.GetElementType() == DbElementTypeInstance.NOZZLE ||
//                        geom.GetElementType() == DbElementTypeInstance.ELCONN ||
//                        geom.GetElementType() == DbElementTypeInstance.SCTN ||
//                        geom.Owner.GetElementType() == DbElementTypeInstance.BRANCH)
//                    {
//                        geometries.AddRange(GetCatalogueGeometry(geom));
//                        continue;
//                    }

//                    var pdmsGeometry = GetItemGeometry(geom, false);
//                    pdmsGeometry.Color = GetItemColor(geom);
//                    if (pdmsGeometry is PdmsExtrusion)
//                    {
//                        var profiles = GetProfilePoints(geom);
//                        var pdmsExtrusion = pdmsGeometry as PdmsExtrusion;
//                        if (profiles.Count == 1)
//                        {
//                            pdmsExtrusion.Profile = profiles[0];
//                            geometries.Add(pdmsExtrusion);
//                        }
//                        else
//                        {
//                            foreach (var profile in profiles)
//                            {
//                                var extrusionGeometry = new PdmsExtrusion(profile, pdmsExtrusion.Height,
//                                    pdmsExtrusion.Matrix, pdmsExtrusion.Negatives)
//                                {
//                                    Color = pdmsGeometry.Color,
//                                    Translucency = pdmsGeometry.Translucency
//                                };
//                                geometries.Add(extrusionGeometry);
//                            }
//                        }
//                        continue;
//                    }

//                    if (pdmsGeometry is PdmsRevolution)
//                    {
//                        var profiles = GetProfilePoints(geom);
//                        var pdmsRevolution = pdmsGeometry as PdmsRevolution;
//                        if (profiles.Count == 1)
//                        {
//                            pdmsRevolution.Profile = profiles[0];
//                            geometries.Add(pdmsRevolution);
//                        }
//                        else
//                        {
//                            foreach (var profile in profiles)
//                            {
//                                var revolutionGeometry = new PdmsRevolution(profile, pdmsRevolution.Angle,
//                                    pdmsRevolution.Matrix, pdmsRevolution.Negatives)
//                                {
//                                    Color = pdmsGeometry.Color,
//                                    Translucency = pdmsGeometry.Translucency
//                                };
//                                geometries.Add(revolutionGeometry);
//                            }
//                        }
//                        continue;
//                    }
//                    geometries.Add(pdmsGeometry);
//                }
//            }
//            else
//            {
//                geometries.AddRange(GetCatalogueGeometry(root));
//            }

//            //TexGeometry = new StringBuilder();
//            //TexGeometry.AppendLine("CREATE PRODUCT");
//            //TexGeometry.AppendLine(
//            //    string.Format("{0} {1}",
//            //        root.GetElementType().ShortName,
//            //        root.GetBool(DbAttributeInstance.ISNAME)
//            //            ? root.GetAsString(DbAttributeInstance.FLNM)
//            //            : root.GetAsString(DbAttributeInstance.REF)));
//            //TexGeometry.AppendLine(root.GetElementType().ShortName);
//            //foreach (var g in geometries)
//            //{
//            //    TexGeometry.Append(g);
//            //    foreach (var negativ in g.Negatives)
//            //    {
//            //        TexGeometry.Append("#");
//            //        TexGeometry.Append(negativ);
//            //    }
//            //    TexGeometry.AppendLine();
//            //    g.Negatives.Clear();
//            //}
//            //geometries.Clear();
//            //TexGeometry.AppendLine("END");
//        }

//        private List<List<double[]>> GetProfilePoints(DbElement element)
//        {
//            var result = new List<List<double[]>>();
//            var opt = CSGTreeBuilder.Instance.CreateCSGTreeBuilderOptions();
//            opt.Pline = false;
//            opt.Holes = false;
//            var csg = CSGTreeBuilder.Instance.GetGeometry(element, opt);
//            if (csg.Length == 0) return result;
//            var item = csg[0];
//            var csgItem = item.Items[0];
//            if (csgItem is CSGFacetGroup)
//            {
//                var list = new List<List<D3Point>>();
//                var facetGroup = csgItem as CSGFacetGroup;
//                foreach (var facet in facetGroup.Facets)
//                {
//                    foreach (var loop in facet.Loops)
//                    {
//                        if (loop.Vertices.Any(v => v.Z > 0)) continue;
//                        var subList = new List<D3Point>();

//                        foreach (var vert in loop.Vertices)
//                        {
//                            var flag = false;
//                            foreach (var v in subList)
//                            {
//                                if (!D3Line.Create(v, vert).CheckLength(0.5))
//                                {
//                                    flag = true;
//                                    break;
//                                }
//                            }
//                            if (flag) continue;
//                            subList.Add(vert);
//                        }
//                        if (subList.Count < 3) continue;
//                        list.Add(subList);
//                    }
//                }

//                foreach (var face in list)
//                {
//                    var profilePoints = new List<double[]>();
//                    foreach (var vert in face)
//                    {
//                        profilePoints.Add(new[]
//                        {
//                            Math.Round(vert.X, 4),
//                            Math.Round(vert.Y, 4),
//                            Math.Round(vert.Z, 4), 0.0
//                        });
//                    }
//                    result.Add(profilePoints);
//                }
//            }

//            return result;
//        }

//        private List<PdmsGeometry> GetCatalogueGeometry(DbElement item)
//        {
//            var color = GetItemColor(item);

//            var res = new List<PdmsGeometry>();
//            var type = item.GetElementType();
//            if (type == DbElementTypeInstance.SCTN)
//            {
//                var sctnRepresentation = GetCatalogueRepresentation(item);
//                var negatives = new List<PdmsGeometry>();
//                foreach (var fitt in item.Members())
//                {
//                    if (fitt.GetElementType() == DbElementTypeInstance.SNODE) continue;
//                    foreach (var tmpl in fitt.Members())
//                    {
//                        foreach (var neg in tmpl.Members())
//                        {
//                            negatives.Add(GetItemGeometry(neg, true));
//                        }
//                    }
//                }

//                foreach (var repr in sctnRepresentation)
//                    repr.Negatives.AddRange(negatives);

//                res.AddRange(sctnRepresentation);

//                //var profiles = GetSctnProfilePoints(item);
//                //var poss = item.GetPosition(DbAttributeInstance.POSS);
//                //var pose = item.GetPosition(DbAttributeInstance.POSE);
//                //var length = poss.Distance(pose);

//                //foreach (var profile in profiles)
//                //{
//                //    var pdmsExtrusion = new PdmsExtrusion(profile, length, GetMatrix(item), negatives);
//                //    res.Add(pdmsExtrusion);
//                //}
//            }
//            else if (type == DbElementTypeInstance.GWALL ||
//                     type == DbElementTypeInstance.FLOOR ||
//                     type == DbElementTypeInstance.PANEL)
//            {
//                var negatives = new List<PdmsGeometry>();
//                foreach (var member in item.Members())
//                {
//                    if (member.GetElementType() == DbElementTypeInstance.PLOOP ||
//                        member.GetElementType() == DbElementTypeInstance.CMPFITTING ||
//                        member.GetElementType() == DbElementTypeInstance.TMPLATE) continue;
//                    negatives.Add(GetItemGeometry(member, true));
//                }
//                if (item.Members(DbElementTypeInstance.PLOOP).Length > 0)
//                {
//                    var pdmsExtrusion = new PdmsExtrusion(item) { Negatives = negatives };
//                    res.Add(pdmsExtrusion);
//                }
//                else
//                {
//                    var tmplates = item.Members(DbElementTypeInstance.TMPLATE);
//                    if (tmplates.Length > 0)
//                    {
//                        var tmpl = tmplates.First();
//                        List<List<List<double[]>>> faceSets;
//                        List<double[]> matrixes;
//                        GetMeshRepresentation(tmpl, out faceSets, out matrixes);
//                        for (var i = 0; i < faceSets.Count; i++)
//                        {
//                            var pdmsFacetGroup = new PdmsFacetGroup(faceSets[i], matrixes[i]);
//                            res.Add(pdmsFacetGroup);
//                        }
//                    }
//                }

//                foreach (var cmpfi in item.Members(DbElementTypeInstance.CMPFITTING))
//                {
//                    foreach (var sbfi in cmpfi.Members(DbElementTypeInstance.SBFITTING))
//                    {
//                        List<List<List<double[]>>> faceSets;
//                        List<double[]> matrixes;
//                        GetMeshRepresentation(sbfi, out faceSets, out matrixes);
//                        for (var i = 0; i < faceSets.Count; i++)
//                        {
//                            var pdmsFacetGroup = new PdmsFacetGroup(faceSets[i], matrixes[i]);
//                            res.Add(pdmsFacetGroup);
//                        }
//                    }
//                }
//            }
//            else if (item.Owner.GetElementType() == DbElementTypeInstance.BRANCH ||
//                     type == DbElementTypeInstance.NOZZLE ||
//                     type == DbElementTypeInstance.ELCONN)
//            {
//                //res.AddRange(GetCSGRepresentation(item));
//                res.AddRange(GetCatalogueRepresentation(item));
//            }
//            else if (type == DbElementTypeInstance.CWBRAN)
//            {
//                res.AddRange(GetElecCSGRepresentation(item));
//            }
//            else if (type == DbElementTypeInstance.CABLE)
//            {
//                //var pointSet = item.GetElementArray(DbAttributeInstance.CBPREF);
//            }

//            foreach (var geom in res)
//                geom.Color = color;

//            return res;
//        }

//        private PdmsGeometry GetItemGeometry(DbElement item, bool isNegative)
//        {
//            PdmsGeometry res = null;
//            var levels = item.GetIntegerArray(DbAttributeInstance.LEVE);
//            var obst = item.GetInteger(DbAttributeInstance.OBST);
//            var type = item.GetElementType();
//            if (type == DbElementTypeInstance.PYRAMID || type == DbElementTypeInstance.NPYRAMID)
//                res = new PdmsPyramid(item);

//            if (type == DbElementTypeInstance.BOX || type == DbElementTypeInstance.NBOX)
//                res = new PdmsBox(item);

//            if (type == DbElementTypeInstance.RTORUS || type == DbElementTypeInstance.NRTORUS)
//                res = new PdmsRectTorus(item);

//            if (type == DbElementTypeInstance.CTORUS || type == DbElementTypeInstance.NCTORUS)
//                res = new PdmsCircTorus(item);

//            if (type == DbElementTypeInstance.DISH || type == DbElementTypeInstance.NDISH)
//            {
//                if (item.GetDouble(DbAttributeInstance.RADI) > 0.0)
//                    res = new PdmsEllipticalDish(item);
//                else
//                    res = new PdmsSphericalDish(item);
//            }

//            if (type == DbElementTypeInstance.SNOUT || type == DbElementTypeInstance.NSNOUT)
//                res = new PdmsSnout(item);

//            if (type == DbElementTypeInstance.SLCYLINDER || type == DbElementTypeInstance.NSLCYLINDER)
//                res = new PdmsSlcy(item);

//            if (type == DbElementTypeInstance.CONE || type == DbElementTypeInstance.NCONE)
//                res = new PdmsCone(item);

//            if (type == DbElementTypeInstance.CYLINDER || type == DbElementTypeInstance.NCYLINDER)
//                res = new PdmsCylinder(item);

//            if (type == DbElementTypeInstance.REVOLUTION || type == DbElementTypeInstance.NREVOLUTION)
//                res = new PdmsRevolution(item);

//            if (type == DbElementTypeInstance.EXTRUSION || type == DbElementTypeInstance.NXTRUSION)
//                res = new PdmsExtrusion(item);

//            //if (item.GetElementType() == DbElementTypeInstance.sphere)
//            //    return new PdmsCylinder(item);
//            if (res == null)
//                throw new NotImplementedException();

//            if (!isNegative)
//            {
//                res.Translucency = (REPRE_LEVEL < levels[0] || REPRE_LEVEL > levels[1]) && obst > 0
//                    ? 75
//                    : 0;
//            }

//            foreach (var negative in item.Members())
//            {
//                if (negative.GetElementType() == DbElementTypeInstance.LOOP) continue;
//                res.Negatives.Add(GetItemGeometry(negative, true));
//            }
//            return res;
//        }

//        private List<PdmsGeometry> GetCatalogueRepresentation(DbElement element)
//        {
//            var result = new Dictionary<int, PdmsGeometry>();
//            var gmref = element.GetElementType() == DbElementTypeInstance.SCTN
//                ? element.GetElement(DbAttributeInstance.GSTR)
//                : element.GetElement(DbAttributeInstance.GMRE);

//            if (gmref.IsNull)
//            {
//                if (element.GetElementType() == DbElementTypeInstance.BEND)
//                {
//                    return new List<PdmsGeometry> { new PdmsCircTorus(element) };
//                }
//                Console.WriteLine("null gmref of {0}" + element.GetAsString(DbAttributeInstance.FLNM));
//            }

//            foreach (var gmItem in gmref.Members())
//            {
//                var representation = GetCatalogueItemRepresentation(element, gmItem);
//                if (representation == null) continue;
//                if (result.ContainsKey(representation.Hash))
//                {
//                    result[representation.Hash].Translucency = Math.Min(result[representation.Hash].Translucency,
//                        representation.Translucency);
//                    continue;
//                }
//                result.Add(representation.Hash, representation);
//            }
//            return result.Values.ToList();
//        }

//        private PdmsGeometry GetCatalogueItemRepresentation(DbElement element, DbElement gmItem, bool negative = false)
//        {
//            PdmsGeometry geom = null;

//            var levels = gmItem.GetIntegerArray(DbAttributeInstance.LEVE);
//            var obst = gmItem.GetInteger(DbAttributeInstance.OBST);
//            if (element.GetElementType() == DbElementTypeInstance.SCTN) obst = 0;
//            if ((REPRE_LEVEL < levels[0] || REPRE_LEVEL > levels[1]) && obst == 0) return null;
//            var gmType = gmItem.GetElementType();
//            if (gmType == DbElementTypeInstance.LINE) return null;


//            if (gmType == DbElementTypeInstance.SCYLINDER || gmType == DbElementTypeInstance.NSCYLINDER ||
//                gmType == DbElementTypeInstance.LCYLINDER || gmType == DbElementTypeInstance.NLCYLINDER ||
//                gmType == DbElementTypeInstance.TUBE)
//            {
//                var cyliGeom = new PdmsCylinder(element, gmItem);
//                if (cyliGeom.Height < 0.1) return null;
//                geom = cyliGeom;
//            }
//            else if (gmType == DbElementTypeInstance.SSLCYLINDER)
//            {
//                geom = new PdmsSlcy(element, gmItem);
//            }
//            else if (gmType == DbElementTypeInstance.LSNOUT)
//            {
//                geom = new PdmsSnout(element, gmItem);
//            }
//            else if (gmType == DbElementTypeInstance.SCTORUS)
//            {
//                geom = new PdmsCircTorus(element, gmItem);
//            }
//            else if (gmType == DbElementTypeInstance.SBOX || gmType == DbElementTypeInstance.NSBOX ||
//                     gmType == DbElementTypeInstance.SRECTANGLE)
//            {
//                geom = new PdmsBox(element, gmItem);
//            }
//            else if (gmType == DbElementTypeInstance.LPYRAMID)
//            {
//                geom = new PdmsPyramid(element, gmItem);
//            }
//            else if (gmType == DbElementTypeInstance.SDSH)
//            {
//                var isElipt = element.EvaluateDouble(
//                    DbExpression.Parse(gmItem.GetAsString(DbAttributeInstance.PRAD)), DbAttributeUnit.NONE) > 0.0;
//                if (isElipt)
//                    geom = new PdmsEllipticalDish(element, gmItem);
//                else
//                    geom = new PdmsSphericalDish(element, gmItem);
//            }
//            else if (gmType == DbElementTypeInstance.SRTORUS)
//            {
//                geom = new PdmsRectTorus(element, gmItem);
//            }
//            else if (gmType == DbElementTypeInstance.SSPHERE)
//            {
//                geom = new PdmsSphere(element, gmItem);
//            }
//            else if (gmType == DbElementTypeInstance.SEXTRUSION || gmType == DbElementTypeInstance.SPROFILE)
//            {
//                geom = new PdmsExtrusion(element, gmItem);
//            }

//            if (geom != null && !negative)
//            {
//                var negatives = new Dictionary<int, PdmsGeometry>();
//                foreach (var negGmItem in gmItem.Members())
//                {
//                    if (negGmItem.GetElementType() == DbElementTypeInstance.SPVERT ||
//                        negGmItem.GetElementType() == DbElementTypeInstance.SLOOP) continue;
//                    var representation = GetCatalogueItemRepresentation(element, negGmItem, true);
//                    if (representation == null) continue;
//                    if (negatives.ContainsKey(representation.Hash)) continue;
//                    negatives.Add(representation.Hash, representation);
//                }
//                geom.Negatives = negatives.Values.ToList();
//                geom.Translucency = (REPRE_LEVEL < levels[0] || REPRE_LEVEL > levels[1]) && obst > 0
//                    ? 75
//                    : 0;
//            }
//            return geom;
//        }

//        private List<DbElement> GetEquiGeometry(DbElement root)
//        {
//            var res = new List<DbElement>();
//            foreach (var member in root.Members())
//            {
//                var memberType = member.GetElementType();
//                if (memberType == DbElementTypeInstance.TMPLATE ||
//                    memberType == DbElementTypeInstance.SUBEQUIPMENT)
//                {
//                    res.AddRange(GetEquiGeometry(member));
//                    continue;
//                }

//                if (memberType == DbElementTypeInstance.DPSET ||
//                    memberType == DbElementTypeInstance.DDSE ||
//                    memberType == DbElementTypeInstance.TMRPSET) continue;
//                res.Add(member);
//            }
//            return res;
//        }

//        private void GetMeshRepresentation(DbElement element, out List<List<List<double[]>>> faceSets,
//            out List<double[]> matrixes)
//        {
//            faceSets = new List<List<List<double[]>>>();
//            matrixes = new List<double[]>();
//            var opt = CSGTreeBuilder.Instance.CreateCSGTreeBuilderOptions();
//            opt.Pline = false;
//            opt.Holes = false;
//            var csg = CSGTreeBuilder.Instance.GetGeometry(element, opt);
//            if (csg.Length == 0) return;

//            foreach (var item in csg)
//            {
//                foreach (var csgItem in item.Items)
//                {
//                    if (csgItem is CSGFacetGroup)
//                    {
//                        var faceSet = new List<List<double[]>>();
//                        var matrix = csgItem.Transform.AsArray12();
//                        var facetGroup = csgItem as CSGFacetGroup;
//                        foreach (var facet in facetGroup.Facets)
//                        {
//                            var face = new List<double[]>();
//                            foreach (var loop in facet.Loops)
//                            {
//                                foreach (var vert in loop.Vertices)
//                                {
//                                    face.Add(new[] { vert.X, vert.Y, vert.Z });
//                                }
//                            }
//                            faceSet.Add(face);
//                        }
//                        faceSets.Add(faceSet);
//                        matrixes.Add(matrix);
//                    }
//                    else
//                    {
//                        Console.WriteLine("wtf");
//                    }
//                }
//            }
//        }

//        private List<PdmsGeometry> GetElecCSGRepresentation(DbElement element)
//        {
//            var result = new Dictionary<int, PdmsGeometry>();
//            var opt = CSGTreeBuilder.Instance.CreateCSGTreeBuilderOptions();
//            opt.Pline = false;
//            opt.Holes = true;
//            var csg = CSGTreeBuilder.Instance.GetGeometry(element, opt);
//            if (csg.Length == 0) return new List<PdmsGeometry>();
//            var type = element.GetElementType();
//            foreach (var item in csg)
//            {
//                if (item.Element.GetElementType() == DbElementTypeInstance.POINTR) continue;
//                foreach (var csgItem in item.Items)
//                {
//                    if (csgItem is CSGLine) continue;

//                    if (csgItem is CSGCylinder)
//                    {
//                        var geom = new PdmsCylinder(csgItem as CSGCylinder);
//                        if (result.ContainsKey(geom.Hash))
//                        {
//                            result[geom.Hash].Translucency = Math.Min(result[geom.Hash].Translucency, geom.Translucency);
//                            continue;
//                        }
//                        result.Add(geom.Hash, geom);
//                    }
//                    else if (csgItem is CSGCTorus)
//                    {
//                        var geom = new PdmsCircTorus(csgItem as CSGCTorus);
//                        if (result.ContainsKey(geom.Hash))
//                        {
//                            result[geom.Hash].Translucency = Math.Min(result[geom.Hash].Translucency, geom.Translucency);
//                            continue;
//                        }
//                        result.Add(geom.Hash, geom);
//                    }
//                    else if (csgItem is CSGRTorus)
//                    {
//                        var geom = new PdmsRectTorus(csgItem as CSGRTorus);
//                        if (result.ContainsKey(geom.Hash))
//                        {
//                            result[geom.Hash].Translucency = Math.Min(result[geom.Hash].Translucency, geom.Translucency);
//                            continue;
//                        }
//                        result.Add(geom.Hash, geom);
//                    }
//                    else if (csgItem is CSGSnout)
//                    {
//                        var snout = csgItem as CSGSnout;
//                        PdmsGeometry geom = null;
//                        if (snout.BottomRadius == snout.TopRadius && snout.BottomXShear == 0.0 &&
//                            snout.BottomYShear == 0.0 && snout.TopXShear == 0.0 && snout.TopYShear == 0.0 &&
//                            snout.XAxisDisplacement == 0.0 && snout.YAxisDisplacement == 0.0)
//                        {
//                            geom = new PdmsCylinder(snout);
//                        }
//                        else if (snout.BottomRadius != snout.TopRadius && snout.BottomXShear == 0.0 &&
//                                 snout.BottomYShear == 0.0 && snout.TopXShear == 0.0 && snout.TopYShear == 0.0 &&
//                                 snout.XAxisDisplacement == 0.0 && snout.YAxisDisplacement == 0.0)
//                        {
//                            geom = new PdmsCone(snout);
//                        }
//                        else if ((snout.BottomRadius == snout.TopRadius) &&
//                                 (snout.BottomXShear != 0.0 || snout.BottomYShear != 0.0 || snout.TopXShear != 0.0 ||
//                                  snout.TopYShear != 0.0))
//                        {
//                            geom = new PdmsSlcy(snout);
//                        }
//                        else if (snout.XAxisDisplacement != 0.0 || snout.YAxisDisplacement != 0.0)
//                        {
//                            geom = new PdmsSnout(snout);
//                        }
//                        else
//                        {
//                            Console.WriteLine("wtf");
//                        }
//                        if (result.ContainsKey(geom.Hash))
//                        {
//                            result[geom.Hash].Translucency = Math.Min(result[geom.Hash].Translucency, geom.Translucency);
//                            continue;
//                        }
//                        result.Add(geom.Hash, geom);
//                    }
//                    else if (csgItem is CSGFacetGroup)
//                    {
//                        var geom = new PdmsFacetGroup(csgItem as CSGFacetGroup);
//                        geom.Translucency = type == DbElementTypeInstance.CWBRAN &&
//                                            item.Element.GetElementType() == DbElementTypeInstance.CWBRAN
//                            ? 75
//                            : geom.Translucency;
//                        if (result.ContainsKey(geom.Hash))
//                        {
//                            result[geom.Hash].Translucency = Math.Min(result[geom.Hash].Translucency, geom.Translucency);
//                            continue;
//                        }
//                        result.Add(geom.Hash, geom);
//                    }
//                    else if (csgItem is CSGBox)
//                    {
//                        var geom = new PdmsBox(csgItem as CSGBox);
//                        if (result.ContainsKey(geom.Hash))
//                        {
//                            result[geom.Hash].Translucency = Math.Min(result[geom.Hash].Translucency, geom.Translucency);
//                            continue;
//                        }
//                        result.Add(geom.Hash, geom);
//                    }
//                    else if (csgItem is CSGPyramid)
//                    {
//                        var geom = new PdmsPyramid(csgItem as CSGPyramid);
//                        if (result.ContainsKey(geom.Hash))
//                        {
//                            result[geom.Hash].Translucency = Math.Min(result[geom.Hash].Translucency, geom.Translucency);
//                            continue;
//                        }
//                        result.Add(geom.Hash, geom);
//                    }
//                    else if (csgItem is CSGSphere)
//                    {
//                        var geom = new PdmsSphere(csgItem as CSGSphere);
//                        if (result.ContainsKey(geom.Hash))
//                        {
//                            result[geom.Hash].Translucency = Math.Min(result[geom.Hash].Translucency, geom.Translucency);
//                            continue;
//                        }
//                        result.Add(geom.Hash, geom);
//                    }
//                    else if (csgItem is CSGDish)
//                    {
//                        var dish = csgItem as CSGDish;
//                        PdmsGeometry geom;
//                        if (dish.Attribute == 0.0)
//                        {
//                            geom = new PdmsSphericalDish(dish);
//                        }
//                        else
//                        {
//                            geom = new PdmsEllipticalDish(dish);
//                        }

//                        if (result.ContainsKey(geom.Hash))
//                        {
//                            result[geom.Hash].Translucency = Math.Min(result[geom.Hash].Translucency, geom.Translucency);
//                            continue;
//                        }
//                        result.Add(geom.Hash, geom);
//                    }
//                    else
//                    {
//                        Console.WriteLine(csgItem.Type);
//                    }
//                }
//            }
//            return result.Values.ToList();
//        }

//        private int GetItemColor(DbElement root)
//        {
//            //foreach (var rule in ColorRules)
//            //{
//            //    var item = DbElement.GetElement();
//            //    var condition = false;
//            //    if (!root.EvaluateValidElement(DbExpression.Parse(rule[0]), ref item) ||
//            //        item.IsNull ||
//            //        !item.EvaluateValidBool(DbExpression.Parse(rule[1]), ref condition) ||
//            //        !condition) continue;

//            //    return int.Parse(rule[2]);
//            //}
//            return 0;
//        }
//    }
//}
