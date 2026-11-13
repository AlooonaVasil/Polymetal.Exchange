using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Aveva.Core.Maths.Geometry;
using Orientation = Aveva.Core.Geometry.Orientation;
using Aveva.Core.Database;
using Aveva.Core.Geometry;
using Aveva.Core3D.CSG;
using System.IO;
using Polymetal.E3D.Ifc.Helpers;

namespace Polymetal.E3D.Ifc
{
    public enum ELEMENT_CUSTOM_COLOR
    {
        NoChanges = 381,
        New = 382,
        Modified = 383,
        Deleted  = 384,
        Preliminary = 385
    }

    public class AvevaGeometry
    {
        internal double[] Matrix;
        internal int Translucency;
        internal int Hash;
        internal int Color;
        public static int CustomColor;
        internal List<AvevaGeometry> Negatives = new List<AvevaGeometry>();
        internal DbElement Root;

        private static List<DbElementType> _negativeTypes;

        internal static List<DbElementType> NegativeTypes
        {
            get
            {
                if (_negativeTypes == null)
                {
                    _negativeTypes = new List<DbElementType>
                    {
                        DbElementTypeInstance.NBOX,
                        DbElementTypeInstance.NCONE,
                        DbElementTypeInstance.NCTORUS,
                        DbElementTypeInstance.NCYLINDER,
                        DbElementTypeInstance.NDISH,
                        DbElementTypeInstance.NLCYLINDER,
                        DbElementTypeInstance.NPOLYHEDRON,
                        DbElementTypeInstance.NPYRAMID,
                        DbElementTypeInstance.NREVOLUTION,
                        DbElementTypeInstance.NRTORUS,
                        DbElementTypeInstance.NSBOX,
                        DbElementTypeInstance.NSCYLINDER,
                        DbElementTypeInstance.NSLCYLINDER,
                        DbElementTypeInstance.NSNOUT,
                        DbElementTypeInstance.NXTRUSION
                    };
                }

                return _negativeTypes;
            }
        }

        internal static DbExpression Expr_PosWrtSite = DbExpression.Parse("POS WRT SITE");
        internal static DbExpression Expr_OriWrtSite = DbExpression.Parse("ORI WRT SITE");

        private static DbElement _currentSite;
        internal static DbElement CurrentSite
        {
            get => _currentSite;
            set
            {
                _currentSite = value;
                _siteMatrix = _currentSite == null
                    ? D3Transform.Create()
                    : D3Transform.Create(GetMatrix(_currentSite)).Inverse();

                _referenceCoordinateSystem = D3Transform.Create();
                if (ReferenceTransformElement.IsNull) 
                    return;

                var qualifer = new DbQualifier { wrtQualifier = ReferenceTransformElement };
                var sitePos = _currentSite.GetPosition(DbAttributeInstance.POS, qualifer);
                var siteOri = _currentSite.GetOrientation(DbAttributeInstance.ORI, qualifer);

                var siteXdir = siteOri.XDir();
                var siteYdir = siteOri.YDir();
                var siteZdir = siteOri.ZDir();

                _referenceCoordinateSystem= D3Transform.Create(new[]
                {
                    siteXdir.East, siteXdir.North, siteXdir.Up,
                    siteYdir.East, siteYdir.North, siteYdir.Up,
                    siteZdir.East, siteZdir.North, siteZdir.Up,
                    Math.Round(sitePos.X, 3), Math.Round(sitePos.Y, 3), Math.Round(sitePos.Z, 3)
                });
            }
        }

        private static D3Transform _siteMatrix;
        internal static D3Transform SiteMatrix => _siteMatrix;

        private static DbElement _referenceElement;

        internal static DbElement ReferenceTransformElement
        {
            get => _referenceElement == null || _referenceElement.IsDeleted
                ? DbElement.GetElement()
                : _referenceElement;
            set => _referenceElement = value;
        }

        private static D3Transform _referenceCoordinateSystem;
        internal static D3Transform ReferenceCoordinateSystem => _referenceCoordinateSystem;

        internal AvevaGeometry()
        {
        }

        internal AvevaGeometry(DbElement root)
        {
            Root = root;
            CustomColor = 0;
        }
        internal AvevaGeometry(DbElement root, int customColor)
        {
            Root = root;
            CustomColor = customColor;
        }

        internal StringBuilder GetText()
        {
            var geometries = new List<AvevaGeometry>();
            var commonNegatives = new List<AvevaGeometry>();
            var matrix = GetMatrix(Root);
            var sb = new StringBuilder();

            if (Root.ElementType == DbElementTypeInstance.EQUIPMENT || Root.ElementType == DbElementTypeInstance.VOLMODEL)
            {
                List<DbElement> equiMembers = GetEquiMembers(Root); //
                //если элемент MagiCAD, преобразовать к FacetGroup
                if (Root.GetAsString(DbAttributeInstance.FUNC) == "MagiCAD")
                {
                    var asFaceExtrusions = new List<DbElement>();
                    var faces = new Dictionary<int, List<List<double[]>>>();
                    foreach (DbElement equiMember in equiMembers)
                    {
                        if (equiMember.ElementType != DbElementTypeInstance.EXTRUSION || equiMember.GetDouble(DbAttributeInstance.HEIG) >= 10.0) continue;
                        var f = new List<double[]>();
                        foreach (var loop in equiMember.Members(DbElementTypeInstance.LOOP))
                        {
                            var vertexes = loop.Members();
                            if (vertexes.Length < 3) break;
                            foreach (var vert in vertexes)
                            {
                                var pos = vert.GetPosition(DbAttributeInstance.POS, new DbQualifier() { wrtQualifier = Root });
                                f.Add(new[] { pos.X, pos.Y, pos.Z });
                            }

                            var p1 = vertexes[0].EvaluatePosition(Expr_PosWrtSite);
                            var p2 = vertexes[1].EvaluatePosition(Expr_PosWrtSite);
                            var p3 = vertexes[2].EvaluatePosition(Expr_PosWrtSite);
                            var vx = Direction.Create(p2, p1);
                            var vy = Direction.Create(p2, p3);
                            var vz = vx.Orthogonal(vy);

                            var extrDirection = equiMember.EvaluateOrientation(Expr_OriWrtSite).ZDir();
                            var angle = extrDirection.Angle(vz);
                            if (angle > 1)
                            {
                                f.Reverse();
                            }

                        }
                        if (f.Count > 0)
                        {
                            var tran = GetTranslucency(equiMember);
                            if (!faces.ContainsKey(tran))
                                faces.Add(tran, new List<List<double[]>>());
                            faces[tran].Add(f);
                        }
                        asFaceExtrusions.Add(equiMember);
                    }
                    foreach (var extr in asFaceExtrusions)
                        equiMembers.Remove(extr);
                    foreach (var pair in faces)
                    {
                        if (pair.Value.Count == 0) continue;
                        var avevaGeometry = new DTO.AvevaFacetGroup(pair.Value, GetMatrix(Root));
                        avevaGeometry.Color = GetItemColor(Root);
                        avevaGeometry.Translucency = pair.Key;
                        geometries.Add(avevaGeometry);
                    }
                }

                var fromImportSite = Root.EvaluateBool(ExportIfc.GetOrAddExpression("PURP OF SITE EQ 'IMP'"));
                
                foreach (var member in equiMembers)
                {
                    if (fromImportSite)
                    {

                        if (NegativeTypes.Contains(member.ElementType))
                        {
                            commonNegatives.Add(GetItemGeometry(member, true));
                            continue;
                        }

                        GetMeshRepresentation(member, out List<List<List<double[]>>> faceSets, out List<double[]> matrixes, out List<AvevaGeometry> additionalGeometry);

                        var memberGeometry = new List<AvevaGeometry>();

                        for (var i = 0; i < faceSets.Count; i++)
                        {
                            var avevaFacetGroup = new DTO.AvevaFacetGroup(faceSets[i], matrixes[i]);

                            avevaFacetGroup.Color = GetItemColor(member);
                            avevaFacetGroup.Translucency = GetTranslucency(member);


                            memberGeometry.Add(avevaFacetGroup);
                        }

                        memberGeometry.AddRange(additionalGeometry);
                                                
                        geometries.AddRange(memberGeometry.AttachDirectNagatives(member));
                        continue;
                    }

                    if (member.ElementType == DbElementTypeInstance.NOZZLE ||
                        member.ElementType == DbElementTypeInstance.EQUCOM ||
                        member.ElementType == DbElementTypeInstance.ELCONN ||
                        member.ElementType == DbElementTypeInstance.SCTN ||
                        member.ElementType == DbElementTypeInstance.PANEL ||
                        member.Owner.ElementType == DbElementTypeInstance.BRANCH)
                    {
                        geometries.AddRange(GetCatalogueGeometry(member));
                        continue;
                    }

                    if (member.Owner.ElementType == DbElementTypeInstance.AIDGROUP)
                    {
                        if (member.ElementType == DbElementTypeInstance.AIDLINE)
                        {
                            //TODO: доделать передачу линий
                            //geometries.Add(new DTO.AvevaLine(member));
                        }
                        continue;
                    }

                    if (member.ElementType == DbElementTypeInstance.DRAWING)
                    {
                        var points = member.Members(DbElementTypeInstance.POINT);
                        
                        for (var i = 0; i < points.Length - 1; i++)
                        {
                            var startPoint = points[i];
                            var endPoint = points[i + 1];

                            var startPos = startPoint.EvaluatePosition(Expr_PosWrtSite); 
                            var endPos = endPoint.EvaluatePosition(Expr_PosWrtSite);

                            var zdir = D3Vector.Create(D3Point.Create(startPos.X, startPos.Y, startPos.Z),
                                D3Point.Create(endPos.X, endPos.Y, endPos.Z));

                            var height = zdir.Magnitude();

                            zdir = zdir.GetUnit();

                            var xdir = zdir.Cross(D3Vector.D3NORTH);
                            if (xdir.IsZero()) 
                                xdir = zdir.Cross(D3Vector.D3EAST);

                            var ydir = zdir.Cross(xdir).GetUnit();

                            xdir = ydir.Cross(zdir).GetUnit();

                            var pointMatrix = new[] {
                                xdir.X, xdir.Y, xdir.Z,
                                ydir.X, ydir.Y, ydir.Z,
                                zdir.X, zdir.Y, zdir.Z,
                                startPos.X, startPos.Y, startPos.Z };

                            geometries.Add(new DTO.AvevaLine(height, pointMatrix));
                        }
                        continue;
                    }                              

                    if (member.ElementType == DbElementTypeInstance.GENPRI ||
                        member.ElementType == DbElementTypeInstance.GENCUR ||
                        member.ElementType == DbElementTypeInstance.GENSEC ||
                        member.ElementType == DbElementTypeInstance.POHEDRON )
                    {
                        GetMeshRepresentation(member, out List<List<List<double[]>>> faceSets, out List<double[]> matrixes, out List<AvevaGeometry> additionalGeometry);
                        for (var i = 0; i < faceSets.Count; i++)
                        {
                            var avevaFacetGroup = new DTO.AvevaFacetGroup(faceSets[i], matrixes[i]);
                            avevaFacetGroup.Color = GetItemColor(member);
                            avevaFacetGroup.Translucency = GetTranslucency(member);
                            geometries.Add(avevaFacetGroup);
                        }
                        geometries.AddRange(additionalGeometry);
                        continue;
                    }

                    AvevaGeometry avevaGeometry = GetItemGeometry(member, false);
                    avevaGeometry.Color = GetItemColor(member);
                    if (avevaGeometry is DTO.AvevaExtrusion avevaExtrusion)
                    {
                        var profiles = GetProfilePoints(member);
                        if (profiles.Count == 1)
                        {
                            avevaExtrusion.Profile = profiles[0];
                            geometries.Add(avevaExtrusion);
                        }
                        else
                        {
                            foreach (var profile in profiles)
                            {
                                var extrusionGeometry = new DTO.AvevaExtrusion(profile, avevaExtrusion.Height,
                                    avevaExtrusion.Matrix, avevaExtrusion.Negatives)
                                {
                                    Color = avevaGeometry.Color,
                                    Translucency = avevaGeometry.Translucency
                                };

                                geometries.Add(extrusionGeometry);
                            }
                        }
                        continue;
                    }

                    if (avevaGeometry is DTO.AvevaRevolution avevaRevolution)
                    {
                        if (avevaRevolution.Profile.Any(x => x.Length > 3 && x[3] != 0.0))
                        {
                            var profiles = GetProfilePoints(member);
                            if (profiles.Count == 1)
                            {
                                avevaRevolution.Profile = profiles[0];
                                geometries.Add(avevaRevolution);
                            }
                            else
                            {
                                foreach (var profile in profiles)
                                {
                                    var revolutionGeometry = new DTO.AvevaRevolution(profile, avevaRevolution.Angle,
                                        avevaRevolution.Matrix, avevaRevolution.Negatives)
                                    {
                                        Color = avevaGeometry.Color,
                                        Translucency = avevaGeometry.Translucency
                                    };

                                    geometries.Add(revolutionGeometry);
                                }
                            }

                            continue;
                        }
                    }

                    if (NegativeTypes.Contains(member.ElementType))
                    {
                        commonNegatives.Add(avevaGeometry);
                    }
                    else
                    {
                        geometries.Add(avevaGeometry);
                    }
                }
            }
            else if (Root.ElementType == DbElementTypeInstance.LINDIMENSION)
            {
                geometries.Add(new DTO.AvevaDimension(Root));
            }
            else
            {
                var catalogueGeometry = GetCatalogueGeometry(Root);
                geometries.AddRange(catalogueGeometry);
            }

            if (geometries.Count == 0) return sb;
            
            if (commonNegatives.Count > 0)
            {
                foreach (var g in geometries)
                {
                    g.Negatives.AddRange(commonNegatives);
                }
            }

            sb.AppendLine(string.Join(" ", matrix.Select(x => x.ToString("0.0000")).ToArray()));
            foreach (var g in geometries)
            {
                AppendGeometry(sb, g);
                foreach (var negativ in g.Negatives)
                {
                    sb.Append("#");
                    AppendGeometry(sb, negativ);
                }
                sb.AppendLine();
                if (g is DTO.AvevaFacetGroup facetGroup && facetGroup.Faces != null)
                    facetGroup.Faces.Clear();
                g.Negatives.Clear();
            }

            foreach (var negativ in commonNegatives)
            {
                if (negativ is DTO.AvevaFacetGroup facetGroup && facetGroup.Faces != null)
                    facetGroup.Faces.Clear();
            }

            commonNegatives.Clear();
            geometries.Clear();
            return sb;
        }

        private static void AppendGeometry(StringBuilder sb, AvevaGeometry geometry)
        {
            if (geometry is DTO.AvevaFacetGroup facetGroup)
            {
                facetGroup.AppendTo(sb);
                return;
            }

            sb.Append(geometry);
        }

        //private string ExportToRvm()
        //{
        //    var macPath = Path.GetTempFileName();
        //    var rvmPath = Path.Combine(Path.GetTempPath(), $"{Root.Ref[0]}-{Root.Ref[1]}.rvm");
        //    using (var sw = new StreamWriter(macPath))
        //    {
        //        sw.WriteLine("EXPORT HOLES ON");
        //        sw.WriteLine("EXPORT AUTOCOLOUR ON");
        //        sw.WriteLine("EXPORT AUTOCOLOUR RESET");
        //        sw.WriteLine("EXPORT ENCODING UTFE");
        //        sw.WriteLine("EXPORT REPRESENTATION ON");

        //        sw.WriteLine($"EXPORT FILE \"{rvmPath}\" text");
        //        sw.WriteLine($"EXPORT {Root.GetAsString(DbAttributeInstance.REF)}");
        //        sw.WriteLine("EXPORT FINISH");
        //    }
        //    Aveva.Core.Utilities.CommandLine.Command.CreateCommand($"$m/{macPath}").Run();

        //    return File.Exists(rvmPath) ? rvmPath : string.Empty;
        //}

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
                        if (loop.Vertices.Any(v => v.Z != 0.0)) continue;
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

        private List<DbElement> GetEquiMembers(DbElement root)
        {
            var res = new List<DbElement>();
            foreach (var member in root.Members())
            {
                var memberType = member.ElementType;
                if (memberType == DbElementTypeInstance.TMPLATE ||
                    memberType == DbElementTypeInstance.SUBEQUIPMENT || 
                    memberType == DbElementTypeInstance.AIDGROUP)
                {
                    res.AddRange(GetEquiMembers(member));
                    continue;
                }

                if (memberType == DbElementTypeInstance.DPSET ||
                    memberType == DbElementTypeInstance.DDSE ||
                    memberType == DbElementTypeInstance.TMRPSET ||
                    memberType == DbElementTypeInstance.DIMGROUP) continue;
                res.Add(member);
            }
            return res;
        }

        internal static AvevaGeometry GetItemGeometry(DbElement item,bool isNegative)
        {
            AvevaGeometry res = null;
            var type = item.ElementType;
            if (type == DbElementTypeInstance.PYRAMID || type == DbElementTypeInstance.NPYRAMID)
                res = new DTO.AvevaPyramid(item);

            if (type == DbElementTypeInstance.BOX || type == DbElementTypeInstance.NBOX)
                res = new DTO.AvevaBox(item);

            if (type == DbElementTypeInstance.RTORUS || type == DbElementTypeInstance.NRTORUS)
                res = new DTO.AvevaRectTorus(item);

            if (type == DbElementTypeInstance.CTORUS || type == DbElementTypeInstance.NCTORUS)
                res = new DTO.AvevaCircTorus(item);

            if (type == DbElementTypeInstance.DISH || type == DbElementTypeInstance.NDISH)
            {
                if (item.GetDouble(DbAttributeInstance.RADI) > 0.0)
                    res = new DTO.AvevaEllipticalDish(item);
                else
                    res = new DTO.AvevaSphericalDish(item);
            }

            if (type == DbElementTypeInstance.SNOUT || type == DbElementTypeInstance.NSNOUT)
                res = new DTO.AvevaSnout(item);

            if (type == DbElementTypeInstance.SLCYLINDER || type == DbElementTypeInstance.NSLCYLINDER)
                res = new DTO.AvevaSlcy(item);

            if (type == DbElementTypeInstance.CONE || type == DbElementTypeInstance.NCONE)
                res = new DTO.AvevaCone(item);

            if (type == DbElementTypeInstance.CYLINDER || type == DbElementTypeInstance.NCYLINDER)
                res = new DTO.AvevaCylinder(item);

            if (type == DbElementTypeInstance.REVOLUTION || type == DbElementTypeInstance.NREVOLUTION)
                res = new DTO.AvevaRevolution(item);

            if (type == DbElementTypeInstance.EXTRUSION || type == DbElementTypeInstance.NXTRUSION)
                res = new DTO.AvevaExtrusion(item);

            if (type == DbElementTypeInstance.PFITTING)
                return new DTO.AvevaBox(item, true);

            if (type == DbElementTypeInstance.POLYHEDRON || type == DbElementTypeInstance.NPOLYHEDRON)
            {
                res = new DTO.AvevaFacetGroup(item);               
            }

            if (res == null)
            {
                using (var sw = new StreamWriter(@"C:\AVEVA\exchange.error", true))
                    sw.WriteLine($"IfcGeometry for type {type.Name} not implemented ({item.GetAsString(DbAttributeInstance.REF)})");
                throw new NotImplementedException();
            }

            if (!isNegative)
            {
                res.Translucency = GetTranslucency(item);
            }

            foreach (var negativeMember in item.Members())
            {
                if (negativeMember.ElementType == DbElementTypeInstance.LOOP || negativeMember.ElementType == DbElementTypeInstance.POLPTLIST || negativeMember.ElementType == DbElementTypeInstance.POLFACE) continue;
                var negative = GetItemGeometry(negativeMember, true);
                res.Negatives.Add(negative);
            }
            return res;
        }

        private static int GetTranslucency(DbElement element)
        {
            if (element.ElementType == DbElementTypeInstance.GENSEC) return 0;
            if (element.ElementType == DbElementTypeInstance.GENPRI && element.Members(DbElementTypeInstance.ARCHIV).Length > 0)
            {
                var archiv = element.Members(DbElementTypeInstance.ARCHIV).First();

                var clrData = archiv.GetAsString(DbAttributeInstance.DESC).Split();                
                if (clrData.Length == 4) return (int)((1 - double.Parse(clrData[3])) * 100);                
            }


            if (element.ElementType == DbElementTypeInstance.BOX) 
            {
                var boxEquipment = element.GetElementArray(DbAttributeInstance.OWNLST)
                    .FirstOrDefault(x => x.ElementType == DbElementTypeInstance.EQUIPMENT);

                if (boxEquipment != null && !boxEquipment.IsNull && boxEquipment.IsValid &&
                    boxEquipment.GetAsString(DbAttributeInstance.DESC) == "Трубный блок")
                    return 0;
            }
            
            var levels = element.GetIntegerArray(DbAttributeInstance.LEVE);
            var obst = element.GetInteger(DbAttributeInstance.OBST);
            if (element.ElementType == DbElementTypeInstance.SCTN) obst = 0;
            return (ExportIfc.REPRE_LEVEL < levels[0] || ExportIfc.REPRE_LEVEL > levels[1]) && obst > 0 ? 75 : 0;
        }

        internal List<AvevaGeometry> GetCatalogueGeometry(DbElement item)
        {
            var color = GetItemColor(item);

            var res = new List<AvevaGeometry>();
            var type = item.ElementType;
            if (type == DbElementTypeInstance.SCTN)
            {
                var sctnRepresentation = new List<AvevaGeometry>();
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
                var negatives = new List<AvevaGeometry>();
                foreach (var fitt in item.Members())
                {
                    if (fitt.ElementType == DbElementTypeInstance.SNODE) continue;
                    foreach (var tmpl in fitt.Members())
                    {
                        foreach (var neg in tmpl.Members())
                        {
                            negatives.Add(GetItemGeometry(neg, true));
                        }
                    }
                }

                foreach (var repr in sctnRepresentation)
                    repr.Negatives.AddRange(negatives);

                res.AddRange(sctnRepresentation);
            }
            else if (type == DbElementTypeInstance.GWALL ||
                     type == DbElementTypeInstance.FLOOR ||
                     type == DbElementTypeInstance.PANEL)
            {
                var negatives = new List<AvevaGeometry>();
                foreach (var member in item.Members())
                {
                    if (member.ElementType == DbElementTypeInstance.PLOOP ||
                        member.ElementType == DbElementTypeInstance.CMPFITTING ||
                        member.ElementType == DbElementTypeInstance.TMPLATE) continue;
                    negatives.Add(GetItemGeometry(member, true));
                }
                if (item.Members(DbElementTypeInstance.PLOOP).Length > 0)
                {
                    var avevaExtrusion = new DTO.AvevaExtrusion(item) { Negatives = negatives };
                    res.Add(avevaExtrusion);
                }

                var tmplates = item.Members(DbElementTypeInstance.TMPLATE);
                if (tmplates.Length > 0)
                {
                    var tmpl = tmplates.First();

                    if (tmpl.Members().Length == tmpl.Members(DbElementTypeInstance.GENPRI).Length)
                    {
                        foreach (var geom in tmpl.Members())
                        {
                            GetMeshRepresentation(geom, out List<List<List<double[]>>> faceSets, out List<double[]> matrixes, out List<AvevaGeometry> additionalGeometry);
                            for (var i = 0; i < faceSets.Count; i++)
                            {
                                var avevaFacetGroup = new DTO.AvevaFacetGroup(faceSets[i], matrixes[i]);
                                avevaFacetGroup.Color = GetItemColor(geom);
                                avevaFacetGroup.Translucency = GetTranslucency(geom);
                                res.Add(avevaFacetGroup);
                            }
                            res.AddRange(additionalGeometry);
                        }
                    }
                    else
                    {
                        var translucency = 0;
                        if (tmpl.Members(DbElementTypeInstance.GENPRI).Length > 0)
                        {
                            translucency = GetTranslucency(tmpl.Members(DbElementTypeInstance.GENPRI).First());
                        }
                        //GetMeshRepresentation(tmpl, out List<List<List<double[]>>> faceSets, out List<double[]> matrixes);
                        //for (var i = 0; i < faceSets.Count; i++)
                        //{
                        //    var avevaFacetGroup = new AvevaFacetGroup(faceSets[i], matrixes[i]);
                        //    avevaFacetGroup.Translucency = translucency;
                        //    avevaFacetGroup.Color = color;
                        //    res.Add(avevaFacetGroup);
                        //}

                        foreach (var memb in tmpl.Members())
                        {
                            if (memb.ElementType == DbElementTypeInstance.GENPRI || 
                                memb.ElementType == DbElementTypeInstance.GENCUR ||
                                memb.ElementType == DbElementTypeInstance.GENSEC || 
                                memb.ElementType == DbElementTypeInstance.POHEDRON)
                            {
                                GetMeshRepresentation(memb, out List<List<List<double[]>>> faceSets, out List<double[]> matrixes, out List<AvevaGeometry> additionalGeometry);
                                for (var i = 0; i < faceSets.Count; i++)
                                {
                                    var avevaFacetGroup = new DTO.AvevaFacetGroup(faceSets[i], matrixes[i]);
                                    avevaFacetGroup.Color = GetItemColor(memb);
                                    avevaFacetGroup.Translucency = GetTranslucency(memb);
                                    res.Add(avevaFacetGroup);
                                }
                                res.AddRange(additionalGeometry);
                                continue;
                            }

                            var avevaGeometry = GetItemGeometry(memb, false);
                            avevaGeometry.Color = GetItemColor(memb);
                            if (avevaGeometry is DTO.AvevaExtrusion avevaExtrusion)
                            {
                                var profiles = GetProfilePoints(memb);
                                if (profiles.Count == 1)
                                {
                                    avevaExtrusion.Profile = profiles[0];
                                    res.Add(avevaExtrusion);
                                }
                                else
                                {
                                    foreach (var profile in profiles)
                                    {
                                        var extrusionGeometry = new DTO.AvevaExtrusion(profile, avevaExtrusion.Height,
                                            avevaExtrusion.Matrix, avevaExtrusion.Negatives)
                                        {
                                            Color = avevaGeometry.Color,
                                            Translucency = avevaGeometry.Translucency
                                        };

                                        res.Add(extrusionGeometry);
                                    }
                                }
                                continue;
                            }

                            if (avevaGeometry is DTO.AvevaRevolution avevaRevolution)
                            {
                                var profiles = GetProfilePoints(memb);
                                if (profiles.Count == 1)
                                {
                                    avevaRevolution.Profile = profiles[0];
                                    res.Add(avevaRevolution);
                                }
                                else
                                {
                                    foreach (var profile in profiles)
                                    {
                                        var revolutionGeometry = new DTO.AvevaRevolution(profile, avevaRevolution.Angle,
                                            avevaRevolution.Matrix, avevaRevolution.Negatives)
                                        {
                                            Color = avevaGeometry.Color,
                                            Translucency = avevaGeometry.Translucency
                                        };

                                        res.Add(revolutionGeometry);
                                    }
                                }
                                continue;
                            }

                            res.Add(avevaGeometry);
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
                                GetMeshRepresentation(memb, out List<List<List<double[]>>> faceSets, out List<double[]> matrixes, out List<AvevaGeometry> additionalGeometry);
                                for (var i = 0; i < faceSets.Count; i++)
                                {
                                    var avevaFacetGroup = new DTO.AvevaFacetGroup(faceSets[i], matrixes[i]);
                                    avevaFacetGroup.Translucency = GetTranslucency(memb);
                                    avevaFacetGroup.Color = color;
                                    res.Add(avevaFacetGroup);
                                }
                                res.AddRange(additionalGeometry);
                            }
                        }
                    }
                }
                return res;
            }
            else if (item.Owner.ElementType == DbElementTypeInstance.BRANCH ||
                     type == DbElementTypeInstance.NOZZLE ||
                     type == DbElementTypeInstance.ELCONN)
            {
                //res.AddRange(GetCSGRepresentation(item));
                res.AddRange(GetCatalogueRepresentation(item));
            }
            else if (type == DbElementTypeInstance.CWBRAN || type == DbElementTypeInstance.EQUCOM)
            {
                res.AddRange(GetElecCSGRepresentation(item));
            }
            else if (type == DbElementTypeInstance.CABLE)
            {
                //var pointSet = item.GetElementArray(DbAttributeInstance.CBPREF);
            }
            else if (type == DbElementTypeInstance.STRFLT || type == DbElementTypeInstance.HANDRA || type==DbElementTypeInstance.GENSEC)
            {
                GetMeshRepresentation(item, out List<List<List<double[]>>> faceSets, out List<double[]> matrixes, out List<AvevaGeometry> additionalGeometry);
                for (var i = 0; i < faceSets.Count; i++)
                {
                    var avevaFacetGroup = new DTO.AvevaFacetGroup(faceSets[i], matrixes[i]);
                    avevaFacetGroup.Translucency = GetTranslucency(item);
                    avevaFacetGroup.Color = color;
                    res.Add(avevaFacetGroup);
                }
                res.AddRange(additionalGeometry);
            }

            foreach (var geom in res)
                geom.Color = color;

            return res;
        }

        internal int GetItemColor(DbElement root)
        {
            if (CustomColor > 0) 
                return CustomColor;

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

        internal List<AvevaGeometry> GetCatalogueRepresentation(DbElement element)
        {
            var result = new Dictionary<int, AvevaGeometry>();
            var gmref = DbElement.GetElement();
            var gmAttrib = element.ElementType == DbElementTypeInstance.SCTN ? DbAttributeInstance.GSTR : DbAttributeInstance.GMRE;

            if (!element.GetElement(DbAttributeInstance.CATR).IsNull)
                gmref = element.GetElement(gmAttrib);

            if (gmref.IsNull)
            {
                if (element.ElementType == DbElementTypeInstance.BEND)
                {
                    if (element.GetDouble(DbAttributeInstance.ANGL) == 0.0) return new List<AvevaGeometry>();
                    return new List<AvevaGeometry> { new DTO.AvevaCircTorus(element) };
                }
                else if (element.ElementType == DbElementTypeInstance.TUBING)
                {
                    var wrtSitePos = element.EvaluatePosition(Expr_PosWrtSite);
                    var wrtSiteOri = element.EvaluateOrientation(Expr_OriWrtSite);
                    var xdir = wrtSiteOri.XDir();
                    var ydir = wrtSiteOri.ZDir().Opposite();
                    var zdir = wrtSiteOri.YDir();
                    var matrix = new[]
                    {
                        xdir.East, xdir.North, xdir.Up,
                        ydir.East, ydir.North, ydir.Up,
                        zdir.East, zdir.North, zdir.Up,
                        wrtSitePos.X,  wrtSitePos.Y, wrtSitePos.Z
                    };
                    return new List<AvevaGeometry> { new DTO.AvevaCylinder(element.GetDouble(DbAttributeInstance.ABOR) / 2, element.GetDouble(DbAttributeInstance.ITLE), matrix) };
                }
                Aveva.Core.Utilities.CommandLine.Command.CreateCommand($"$Pnull gmref of {element.GetAsString(DbAttributeInstance.FLNM)}").Run();
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

        internal AvevaGeometry GetCatalogueItemRepresentation(DbElement element, DbElement gmItem, bool negative=false)
        {
            AvevaGeometry geom = null;

            var levels = gmItem.GetIntegerArray(DbAttributeInstance.LEVE);
            var obst = gmItem.GetInteger(DbAttributeInstance.OBST);
            if (element.ElementType == DbElementTypeInstance.SCTN) obst = 0;
            if ((ExportIfc.REPRE_LEVEL < levels[0] || ExportIfc.REPRE_LEVEL > levels[1]) && obst ==0) return null;
            var gmType = gmItem.ElementType;
            if (gmType == DbElementTypeInstance.LINE) return null;


            if (gmType == DbElementTypeInstance.SCYLINDER || gmType == DbElementTypeInstance.NSCYLINDER ||
                gmType == DbElementTypeInstance.LCYLINDER || gmType == DbElementTypeInstance.NLCYLINDER ||
                gmType == DbElementTypeInstance.TUBE)
            {
                var cyliGeom = new DTO.AvevaCylinder(element, gmItem);
                if (cyliGeom.Height < 0.1) return null;
                geom = cyliGeom;
            }
            else if (gmType == DbElementTypeInstance.SSLCYLINDER)
            {
                geom = new DTO.AvevaSlcy(element, gmItem);
            }
            else if (gmType == DbElementTypeInstance.LSNOUT)
            {
                geom = new DTO.AvevaSnout(element, gmItem);
            }
            else if (gmType == DbElementTypeInstance.SCTORUS)
            {
                geom = new DTO.AvevaCircTorus(element, gmItem);
            }
            else if (gmType == DbElementTypeInstance.BOXING || gmType == DbElementTypeInstance.SBOX || gmType == DbElementTypeInstance.NSBOX || gmType == DbElementTypeInstance.SRECTANGLE)
            {
                geom = new DTO.AvevaBox(element, gmItem);
            }
            else if (gmType == DbElementTypeInstance.LPYRAMID)
            {
                geom = new DTO.AvevaPyramid(element, gmItem);
            }
            else if (gmType == DbElementTypeInstance.SDSH)
            {
                var isElipt = element.EvaluateDouble(ExportIfc.GetOrAddExpression(gmItem.GetAsString(DbAttributeInstance.PRAD)), DbAttributeUnit.NONE) > 0.0;
                if (isElipt)
                    geom = new DTO.AvevaEllipticalDish(element, gmItem);
                else
                    geom = new DTO.AvevaSphericalDish(element, gmItem);
            }
            else if (gmType == DbElementTypeInstance.SRTORUS)
            {
                geom = new DTO.AvevaRectTorus(element, gmItem);
            }
            else if (gmType == DbElementTypeInstance.SSPHERE)
            {
                geom = new DTO.AvevaSphere(element, gmItem);
            }
            else if (gmType == DbElementTypeInstance.SEXTRUSION || gmType == DbElementTypeInstance.SPROFILE)
            {
                geom = new DTO.AvevaExtrusion(element, gmItem);
            }

            if (geom != null && !negative)
            {
                var negatives = new Dictionary<int, AvevaGeometry>();
                foreach (var negGmItem in gmItem.Members())
                {
                    if (negGmItem.ElementType == DbElementTypeInstance.SPVERT ||
                        negGmItem.ElementType == DbElementTypeInstance.SLOOP) continue;
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

        internal static double[] GetMatrix(DbElement item)
        {
            if (item.ElementType == DbElementTypeInstance.SITE)
            {
                var sitePos = item.GetPosition(DbAttributeInstance.POS);
                var siteOri = item.GetOrientation(DbAttributeInstance.ORI);

                var siteXdir = siteOri.XDir();
                var siteYdir = siteOri.YDir();
                var siteZdir = siteOri.ZDir();

                return new[]
                {
                    siteXdir.East, siteXdir.North, siteXdir.Up,
                    siteYdir.East, siteYdir.North, siteYdir.Up,
                    siteZdir.East, siteZdir.North, siteZdir.Up,
                    Math.Round(sitePos.X,3),  Math.Round(sitePos.Y,3), Math.Round(sitePos.Z,3)
                };
            }

            if (item.ElementType == DbElementTypeInstance.CABLE)
                return new[]
                {
                    0.0, 0.0, 0.0,
                    0.0, 0.0, 0.0,
                    0.0, 0.0, 0.0,
                    0.0, 0.0, 0.0
                };

            var wrtSitePos = item.EvaluatePosition(Expr_PosWrtSite);
            var wrtSiteOri = item.EvaluateOrientation(Expr_OriWrtSite);
            var xdir = wrtSiteOri.XDir();
            var ydir = wrtSiteOri.YDir();
            var zdir = wrtSiteOri.ZDir();
            var m = new[]
            {
                xdir.East, xdir.North, xdir.Up,
                ydir.East, ydir.North, ydir.Up,
                zdir.East, zdir.North, zdir.Up,
                Math.Round(wrtSitePos.X,3),  Math.Round(wrtSitePos.Y,3), Math.Round(wrtSitePos.Z,3)
            };

            if (item.ElementType == DbElementTypeInstance.FLOOR && item.Members(DbElementTypeInstance.PLOOP).Length > 0)
            {                
                var ploo = item.Members(DbElementTypeInstance.PLOOP).First();
                var sjus = ploo.GetAsString(DbAttributeInstance.SJUS).ToUpper();                
                if (sjus == "UCEN" || sjus == "UTOP")
                {
                    var height = ploo.GetDouble(DbAttributeInstance.HEIG);
                    var transform = D3Transform.Create(m);

                    var p0 = sjus == "UCEN" ? D3Point.Create(0, 0, -height / 2) : D3Point.Create(0, 0, -height);

                    var transformPoint = transform.Multiply(p0);
                    m[9] = transformPoint.X;
                    m[10] = transformPoint.Y;
                    m[11] = transformPoint.Z;
                }              
                else if (sjus != "UNSET" && sjus != "DBOT")
                {
                    Aveva.Core.Utilities.CommandLine.Command.CreateCommand($"$P{item} has SJUS {sjus}").Run();
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

        internal void GetMeshRepresentation(DbElement element, out List<List<List<double[]>>> faceSets, out List<double[]> matrixes, out List<AvevaGeometry> additionalGeometry)
        {
            additionalGeometry = new List<AvevaGeometry>();
            faceSets = new List<List<List<double[]>>>();
            matrixes = new List<double[]>();
            var opt = CSGTreeBuilder.Instance.CreateCSGTreeBuilderOptions();
            opt.Pline = false;
            opt.Holes = true;         

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
                    else if (csgItem is CSGExtrusion)
                    {
                        var faceSet = new List<List<double[]>>();
                        var matrix = csgItem.Transform.AsArray12();
                        var facetGroup = (csgItem as CSGExtrusion).AsFacetGroup();
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
                    else if (csgItem is CSGBox csgBox)
                    {
                        additionalGeometry.Add(Normalize(new DTO.AvevaBox(csgBox)));
                    }
                    else if (csgItem is CSGCTorus csgCTorus)
                    {
                        additionalGeometry.Add(Normalize(new DTO.AvevaCircTorus(csgCTorus)));
                    }
                    else if (csgItem is CSGRTorus csgRTorus)
                    {
                        additionalGeometry.Add(Normalize(new DTO.AvevaRectTorus(csgRTorus)));
                    }
                    else if (csgItem is CSGCylinder csgCylinder)
                    {
                        additionalGeometry.Add(Normalize(new DTO.AvevaCylinder(csgCylinder)));
                    }
                    else if (csgItem is CSGPyramid csgPyramid)
                    {
                        additionalGeometry.Add(Normalize(new DTO.AvevaPyramid(csgPyramid)));
                    }
                    else
                    {
                        Aveva.Core.Utilities.CommandLine.Command.CreateCommand($"$Pwtf GetMeshRepresentation").Run();
                    }
                }
            }

           
        }
        private AvevaGeometry Normalize<T>(in T geometry)
            where T: AvevaGeometry
        {
            geometry.Matrix = SiteMatrix.Multiply(D3Transform.Create(geometry.Matrix)).AsArray12();
            return geometry;
        }       

        internal List<AvevaGeometry> GetElecCSGRepresentation(DbElement element)
        {
            var result = new Dictionary<int, AvevaGeometry>();
            var opt = CSGTreeBuilder.Instance.CreateCSGTreeBuilderOptions();
            opt.Pline = false;
            opt.Holes = true;
            var csg = CSGTreeBuilder.Instance.GetGeometry(element, opt);
            if (csg.Length == 0) return new List<AvevaGeometry>();
            var type = element.ElementType;
            foreach (var item in csg)
            {
                if (item.Element.ElementType == DbElementTypeInstance.POINTR || item.Element.ElementType == DbElementTypeInstance.RNODE) continue;
                foreach (var csgItem in item.Items)
                {
                    if (csgItem is CSGLine) continue;
                    
                    if (csgItem is CSGCylinder)
                    {
                        var geom = new DTO.AvevaCylinder(csgItem as CSGCylinder);
                        geom.Matrix = SiteMatrix.Multiply(D3Transform.Create(geom.Matrix)).AsArray12();
                        if (result.ContainsKey(geom.Hash))
                        {
                            result[geom.Hash].Translucency = Math.Min(result[geom.Hash].Translucency, geom.Translucency);
                            continue;
                        }
                        result.Add(geom.Hash, geom);
                    }
                    else if (csgItem is CSGCTorus)
                    {
                        var geom = new DTO.AvevaCircTorus(csgItem as CSGCTorus);
                        if (result.ContainsKey(geom.Hash))
                        {
                            result[geom.Hash].Translucency = Math.Min(result[geom.Hash].Translucency, geom.Translucency);
                            continue;
                        }
                        result.Add(geom.Hash, geom);
                    }
                    else if (csgItem is CSGRTorus)
                    {
                        var geom = new DTO.AvevaRectTorus(csgItem as CSGRTorus);
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
                        AvevaGeometry geom = null;
                        if (snout.BottomRadius == snout.TopRadius && snout.BottomXShear == 0.0 &&
                            snout.BottomYShear == 0.0 && snout.TopXShear == 0.0 && snout.TopYShear == 0.0 &&
                            snout.XAxisDisplacement == 0.0 && snout.YAxisDisplacement == 0.0)
                        {
                            geom = new DTO.AvevaCylinder(snout);
                        }
                        else if (snout.BottomRadius != snout.TopRadius && snout.BottomXShear == 0.0 &&
                                 snout.BottomYShear == 0.0 && snout.TopXShear == 0.0 && snout.TopYShear == 0.0 &&
                                 snout.XAxisDisplacement == 0.0 && snout.YAxisDisplacement == 0.0)
                        {
                            geom = new DTO.AvevaCone(snout);
                        }
                        else if ((snout.BottomRadius == snout.TopRadius) && (snout.BottomXShear != 0.0 || snout.BottomYShear != 0.0 || snout.TopXShear != 0.0 || snout.TopYShear != 0.0))
                        {
                            geom = new DTO.AvevaSlcy(snout);
                        }
                        else if (snout.XAxisDisplacement != 0.0 || snout.YAxisDisplacement != 0.0)
                        {
                            geom = new DTO.AvevaSnout(snout);
                        }
                        else 
                        {
                            Aveva.Core.Utilities.CommandLine.Command.CreateCommand($"$Pwtf GetElecCSGRepresentation").Run();                          
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
                        var geom = new DTO.AvevaFacetGroup(csgItem as CSGFacetGroup);
                        geom.Translucency = type == DbElementTypeInstance.CWBRAN &&
                                            item.Element.ElementType == DbElementTypeInstance.CWBRAN
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
                        var geom = new DTO.AvevaBox(csgItem as CSGBox);
                        geom.Matrix = SiteMatrix.Multiply(D3Transform.Create(geom.Matrix)).AsArray12();
                        if (result.ContainsKey(geom.Hash))
                        {
                            result[geom.Hash].Translucency = Math.Min(result[geom.Hash].Translucency, geom.Translucency);
                            continue;
                        }
                        result.Add(geom.Hash, geom);
                    }
                    else if (csgItem is CSGPyramid)
                    {
                        var geom = new DTO.AvevaPyramid(csgItem as CSGPyramid);
                        if (result.ContainsKey(geom.Hash))
                        {
                            result[geom.Hash].Translucency = Math.Min(result[geom.Hash].Translucency, geom.Translucency);
                            continue;
                        }
                        result.Add(geom.Hash, geom);
                    }
                    else if (csgItem is CSGSphere)
                    {
                        var geom = new DTO.AvevaSphere(csgItem as CSGSphere);
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
                        AvevaGeometry geom;
                        if (dish.Attribute == 0.0)
                        {
                            geom = new DTO.AvevaSphericalDish(dish);
                        }
                        else
                        {
                            geom = new DTO.AvevaEllipticalDish(dish);
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
                        Aveva.Core.Utilities.CommandLine.Command.CreateCommand($"$P{csgItem.Type}").Run();                   
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

            if (expression.Contains(Core.Common.UserAttributes.PolyInsuThickness.Name))
            {
                var insuThickness = root.GetDouble(Core.Common.UserAttributes.PolyInsuThickness);
                if (insuThickness <= 0) return 0;
                expression = expression.Replace(Core.Common.UserAttributes.PolyInsuThickness.Name, insuThickness.ToString());
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
                        Aveva.Core.Utilities.CommandLine.Command.CreateCommand($"$PCan't evaluate \"{expression}\" at {root.GetAsString(DbAttributeInstance.FLNM)}").Run();
                    }
                    res += value;
                }
            }
            else if (expression.Contains("SUM "))
            {
                var parts = expression.Split(new [] {' '}, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 5)
                {
                    Aveva.Core.Utilities.CommandLine.Command.CreateCommand($"$PCheck expression \"{expression}\" on {root.GetAsString(DbAttributeInstance.FLNM)}").Run();
                }
                var value = 0.0;
                var exp = $"{parts[1]} {parts[2]}";
                if (!root.EvaluateValidDouble(ExportIfc.GetOrAddExpression(exp), DbAttributeUnit.NONE, ref value) && exp != "IPARAM 1")
                {
                    Aveva.Core.Utilities.CommandLine.Command.CreateCommand($"$PCan't evaluate \"{exp}\" at {root.GetAsString(DbAttributeInstance.FLNM)}").Run();
                }
                res += value;
                value = 0.0;
                exp = $"{parts[3]} {parts[4]}";
                if (!root.EvaluateValidDouble(ExportIfc.GetOrAddExpression(exp), DbAttributeUnit.NONE, ref value) && exp!="IPARAM 1")
                {
                    Aveva.Core.Utilities.CommandLine.Command.CreateCommand($"$PCan't evaluate \"{exp}\" at {root.GetAsString(DbAttributeInstance.FLNM)}").Run();
                }
                res += value;
            }
            else
            {
                if (!root.EvaluateValidDouble(ExportIfc.GetOrAddExpression(expression), DbAttributeUnit.NONE, ref res))
                {
                    Aveva.Core.Utilities.CommandLine.Command.CreateCommand($"$PCan't evaluate \"{expression}\" at {root.GetAsString(DbAttributeInstance.FLNM)}").Run();
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
                direction = root.GetDirection(DbAttributeInstance.PDIR, new DbQualifier() { doubleQualifiers = new[] { double.Parse(expression) }, wrtQualifier = root });
                position = root.GetPosition(DbAttributeInstance.PPOS, new DbQualifier() { doubleQualifiers = new[] { double.Parse(expression) }, wrtQualifier = root });
            }
            else
            {
                direction = Direction.Create("E");
                if (!root.EvaluateValidDirection(ExportIfc.GetOrAddExpression(expression), ref direction))
                {
                    Aveva.Core.Utilities.CommandLine.Command.CreateCommand($"$PCan't evaluate \"{expression}\" at {root.GetAsString(DbAttributeInstance.FLNM)}").Run();
                }
            }

            if (opposite)
                direction = direction.Opposite();
        }
    }
}