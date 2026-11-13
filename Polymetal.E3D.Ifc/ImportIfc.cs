using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aveva.Core.Database;
using Aveva.Core.Database.Filters;
using Aveva.Core.Geometry;
using Aveva.Core.PMLNet;
using Xbim.Common.Geometry;
using Xbim.Ifc;
using Xbim.Ifc2x3.Extensions;
using Xbim.Ifc2x3.GeometryResource;
using Xbim.Ifc2x3.ProductExtension;
using Xbim.ModelGeometry.Scene;
using cmd = Aveva.Core.Utilities.CommandLine.Command;

namespace Polymetal.E3D.Ifc
{
    [PMLNetCallable()]
    public class ImportIfc
    {
        [PMLNetCallable()]
        public ImportIfc()
        {
        }

        [PMLNetCallable()]
        public void Assign(ImportIfc that)
        {

        }

        [PMLNetCallable()]
        public void Run(string path, string destination)
        {
            if (!File.Exists(path))
                return;

            var destElement = DbElement.GetElement(destination);
            if (destElement.IsNull)
                return;

            var site = destElement.EvaluateElement(DbExpression.Parse("SITE"));
            var tmpSite = site.CreateAfter(DbElementTypeInstance.SITE);
            tmpSite.SetAttribute(DbAttributeInstance.POS, site.GetPosition(DbAttributeInstance.POS));
            tmpSite.SetAttribute(DbAttributeInstance.ORI, site.GetOrientation(DbAttributeInstance.ORI));

            cmd.CreateCommand($"{tmpSite.GetAsString(DbAttributeInstance.REF)} IMP IFC \"{path}\"").Run();

            CreateGrids(path, destElement);
            
            var parentDictionary = new Dictionary<DbElement, DbElement>();
            DbElement equi;
            var qualifier = new DbQualifier { wrtQualifier = destElement };
            foreach (var zone in tmpSite.Members())
            {
                var genpriCollection = new DBElementCollection(zone, new TypeFilter(DbElementTypeInstance.GENPRI)).Cast<DbElement>().ToList();
                foreach (var genpri in genpriCollection)
                {
                    if (!parentDictionary.ContainsKey(genpri.Owner))
                    {
                        equi = destElement.CreateLast(DbElementTypeInstance.EQUIPMENT);
                        equi.SetAttribute(DbAttributeInstance.DESC, genpri.Owner.GetString(DbAttributeInstance.DESC));
                        equi.SetAttribute(DbAttributeInstance.POS, genpri.Owner.GetPosition(DbAttributeInstance.POS, qualifier));
                        equi.SetAttribute(DbAttributeInstance.ORI, genpri.Owner.GetOrientation(DbAttributeInstance.ORI, qualifier));
                        parentDictionary.Add(genpri.Owner, equi);
                    }
                    else
                    {
                        equi = parentDictionary[genpri.Owner];
                    }

                    genpri.InsertAfterLast(equi);
                }
            }

            tmpSite.Delete();

            cmd.CreateCommand("$Pimport finished").RunInPdms();
        }

        [PMLNetCallable()]
        public void RunMultiCAD(string path, string destination)
        {
            if (!File.Exists(path))
                return;

            var destElement = DbElement.GetElement(destination);
            if (destElement.IsNull)
                return;

            var site = destElement.EvaluateElement(DbExpression.Parse("SITE"));
            var tmpSite = site.CreateAfter(DbElementTypeInstance.SITE);
            tmpSite.SetAttribute(DbAttributeInstance.POS, site.GetPosition(DbAttributeInstance.POS));
            tmpSite.SetAttribute(DbAttributeInstance.ORI, site.GetOrientation(DbAttributeInstance.ORI));

            cmd.CreateCommand($"IMP MULTICAD \"{path}\" AS EQUI TO {tmpSite.GetAsString(DbAttributeInstance.REF)}")
                .Run();

            try
            {
                CreateGrids(path, destElement);
            }
            catch (Exception e)
            {
                cmd.CreateCommand($"$PException: {e.Message}").Run();
            }

            DbElement equi;
            var qualifier = new DbQualifier { wrtQualifier = destElement };
            var subeCollection = new DBElementCollection(tmpSite, new TypeFilter(DbElementTypeInstance.SUBEQUIPMENT))
                .Cast<DbElement>().ToList();
            foreach (var sube in subeCollection)
            {
                if (sube.Members().Length == 0)
                    continue;

                equi = destElement.CreateLast(DbElementTypeInstance.EQUIPMENT);
                equi.SetAttribute(DbAttributeInstance.DESC, sube.GetString(DbAttributeInstance.DESC));
                equi.SetAttribute(DbAttributeInstance.POS, sube.GetPosition(DbAttributeInstance.POS, qualifier));
                equi.SetAttribute(DbAttributeInstance.ORI, sube.GetOrientation(DbAttributeInstance.ORI, qualifier));

                foreach (var member in sube.Members())
                {
                    member.InsertAfterLast(equi);
                }
            }

            tmpSite.Delete();

            cmd.CreateCommand("$Pimport finished").RunInPdms();
        }

        private void CreateGrids(string path, DbElement root)
        {
            if (!root.IsCreatable(DbElementTypeInstance.AIDGROUP))
                return;

            var model = IfcStore.Open(path);
            var context = new Xbim3DModelContext(model);
            context.CreateContext();
            
            foreach (var grid in model.Instances.OfType<IfcGrid>())
            {
                var axes = new List<Xbim.Ifc2x3.GeometricConstraintResource.IfcGridAxis>();
                axes.AddRange(grid.UAxes);
                axes.AddRange(grid.VAxes);
                axes.AddRange(grid.WAxes);
                if (axes.Count == 0)
                    continue;

                var aidgroup = root.CreateLast(DbElementTypeInstance.AIDGROUP);
                var matrix = grid.Transform();
                var aidgroupPos = matrix.Transform(new XbimPoint3D());
                aidgroup.SetAttribute(DbAttributeInstance.POS, Position.Create(aidgroupPos.X, aidgroupPos.Y, aidgroupPos.Z));

                foreach (var axe in axes)
                {
                    if (axe.AxisCurve is IfcPolyline ifcPolyLine && ifcPolyLine.Points.Count > 1)
                    {
                        var aidtext = aidgroup.CreateLast(DbElementTypeInstance.AIDTEXT);
                        aidtext.SetAttribute(DbAttributeInstance.CHEI, 1000.0);
                        aidtext.SetAttribute(DbAttributeInstance.STEX, axe.AxisTag?.Value.ToString());

                        var x = double.IsNaN(ifcPolyLine.Points[0].X) ? 0.0 : Math.Round(ifcPolyLine.Points[0].X, 2);
                        var y = double.IsNaN(ifcPolyLine.Points[0].Y) ? 0.0 : Math.Round(ifcPolyLine.Points[0].Y, 2);
                        aidtext.SetAttribute(DbAttributeInstance.POS, Position.Create(x, y, 0.0));

                        for (var i = 0; i < ifcPolyLine.Points.Count - 1; i++)
                        {
                            var aidline = aidgroup.CreateLast(DbElementTypeInstance.AIDLINE);
                            x = double.IsNaN(ifcPolyLine.Points[i].X) ? 0.0 : Math.Round(ifcPolyLine.Points[i].X, 2);
                            y = double.IsNaN(ifcPolyLine.Points[i].Y) ? 0.0 : Math.Round(ifcPolyLine.Points[i].Y, 2);

                            aidline.SetAttribute(DbAttributeInstance.POSS, Position.Create(x,y,0.0));

                            x = double.IsNaN(ifcPolyLine.Points[i + 1].X) ? 0.0 : Math.Round(ifcPolyLine.Points[i + 1].X, 2);
                            y = double.IsNaN(ifcPolyLine.Points[i + 1].Y) ? 0.0 : Math.Round(ifcPolyLine.Points[i + 1].Y, 2);

                            aidline.SetAttribute(DbAttributeInstance.POSE, Position.Create(x, y, 0.0));
                        }
                    }
                }
            }
        }
    }
}
