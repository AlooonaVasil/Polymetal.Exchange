using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xbim.Common.Geometry;
using Xbim.Ifc2x3.GeometricConstraintResource;
using Xbim.Ifc2x3.GeometryResource;
using Xbim.Ifc2x3.MeasureResource;
using Xbim.Ifc2x3.PresentationAppearanceResource;
using Xbim.Ifc2x3.PresentationDefinitionResource;
using Xbim.Ifc2x3.PresentationResource;
using Xbim.Ifc2x3.ProductExtension;
using Xbim.Ifc2x3.RepresentationResource;
using Xbim.IO;

namespace Polymetal.E3D.IfcServer
{
    public class TempClassForLabel
    {
        public static void CreateModelWithLabel()
        {
            EntityCreator.Init();
            var modelStore = EntityCreator.CreateModel();
            using (var tr = modelStore.BeginTransaction("Initialise Project"))
            {
                EntityCreator.CreateProject("Model");
                tr.Commit();
            }

            var siteName = "TestLabel";
            IfcSite currentSite;
            using (var tr = modelStore.BeginTransaction("Initialise Site " + siteName))
            {
                currentSite = EntityCreator.CreateSite(siteName, new string[3] { "0,0", "0,0", "0,0"});
                //var building = EntityCreator.CreateBuilding(currentSite);
                //buildingStorey = EntityCreator.CreateBuildingStorey(building);

                tr.Commit();
            }

            using (var tr = modelStore.BeginTransaction("New Text Label"))
            {
                var relativePlacement = EntityCreator.CreateAxis2Placement3D(
                                new XbimPoint3D(0, 0, 0),
                                new XbimVector3D(0.0, 0.0, 1.0),
                                new XbimVector3D(1.0, 0.0, 0.0),
                                XbimMatrix3D.Identity
                );

                var placement = EntityCreator.CreateLocalPlacement(
                    Representation.RootMatrix.Transform(new XbimPoint3D(0.0, 0.0, 0.0)), 
                    Representation.RootMatrix.Transform(new XbimVector3D(0.0, 0.0, 1.0)),
                    Representation.RootMatrix.Transform(new XbimVector3D(1.0, 0.0, 0.0)), 
                    currentSite.ObjectPlacement
                );

                IfcTextLiteralWithExtent textLiteralWithExtent = GetTextLiteralWithExtent(modelStore, relativePlacement);
                IfcShapeRepresentation ifcShapeRepresentation = GetShapeRepresentation(modelStore, textLiteralWithExtent);

                IfcProductDefinitionShape productDefinitionShape = modelStore.Instances.New<IfcProductDefinitionShape>();
                productDefinitionShape.Representations.Add(ifcShapeRepresentation);

                IfcAnnotation annotation = modelStore.Instances.New<IfcAnnotation>();
                annotation.Representation = productDefinitionShape;
                annotation.Name = "Test label";
                annotation.ObjectPlacement = placement;

                currentSite.AddElement(annotation);
                
                tr.Commit();
            }

            var filePath = "C:\\Users\\ivanetcas\\test_label.ifc";
            
            modelStore.SaveAs(filePath, IfcStorageType.Ifc);
            modelStore.Close();
            modelStore.Dispose();
        }

        private static IfcShapeRepresentation GetShapeRepresentation(Xbim.Ifc.IfcStore modelStore, IfcTextLiteralWithExtent textLiteralWithExtent)
        {
            var ifcRepresentationContext = modelStore.Instances.New<IfcRepresentationContext>();
            var ifcShapeRepresentation = modelStore.Instances.New<IfcShapeRepresentation>();
            ifcShapeRepresentation.Items.Add(textLiteralWithExtent);
            ifcShapeRepresentation.RepresentationIdentifier = new Xbim.Ifc2x3.MeasureResource.IfcLabel("Annotation");
            ifcShapeRepresentation.RepresentationType = new Xbim.Ifc2x3.MeasureResource.IfcLabel("Annotation2D");
            ifcShapeRepresentation.ContextOfItems = ifcRepresentationContext;
            return ifcShapeRepresentation;
        }

        private static IfcTextLiteralWithExtent GetTextLiteralWithExtent(Xbim.Ifc.IfcStore modelStore, IfcAxis2Placement placement)
        {
            IfcPresentableText presentableText = new IfcPresentableText("Testing notes here");
            IfcPlanarExtent planarExtent = modelStore.Instances.New<IfcPlanarExtent>(extent =>
            {
                extent.SizeInX = 1000;
                extent.SizeInY = 100;
            });

            IfcColourRgb blackColour = modelStore.Instances.New<IfcColourRgb>( colour =>
            {
                colour.Name = new IfcLabel("BlackColor");
                colour.Red = new IfcNormalisedRatioMeasure(255);
                colour.Green = new IfcNormalisedRatioMeasure(255);
                colour.Blue = new IfcNormalisedRatioMeasure(255);
            });

            IfcColourRgb whiteColour = modelStore.Instances.New<IfcColourRgb>(colour =>
            {
                colour.Name = new IfcLabel("WhiteColor");
                colour.Red = new IfcNormalisedRatioMeasure(255);
                colour.Green = new IfcNormalisedRatioMeasure(255);
                colour.Blue = new IfcNormalisedRatioMeasure(255);
            });

            IfcTextStyleForDefinedFont styleForDefinedFont = modelStore.Instances.New<IfcTextStyleForDefinedFont>(style => 
            {
                style.BackgroundColour = whiteColour;
                style.Colour = blackColour;
            });

            IfcTextStyleFontModel styleFontModel = modelStore.Instances.New<IfcTextStyleFontModel>(fontModel => 
            {
                fontModel.Name = new IfcLabel("FontModel");
                fontModel.FontStyle = new IfcFontStyle();
                fontModel.FontVariant = new IfcFontVariant();
                fontModel.FontWeight = new IfcFontWeight();
                fontModel.FontSize = new IfcNormalisedRatioMeasure(72);
            });

            IfcTextStyle textStyle = modelStore.Instances.New<IfcTextStyle>(style => 
            {
                style.Name = new IfcLabel("TestTextStyle");
                style.TextCharacterAppearance = styleForDefinedFont;
                style.TextFontStyle = styleFontModel;
                style.TextFontStyle = styleFontModel;

            });

            var styleAssigment = modelStore.Instances.New<IfcPresentationStyleAssignment>(assigment =>
            {
                assigment.Styles.Add(textStyle);
            });

           

            IfcTextLiteralWithExtent textLiteralWithExtent = modelStore.Instances.New<IfcTextLiteralWithExtent>();
            textLiteralWithExtent.Literal = presentableText;
            textLiteralWithExtent.Placement = placement;
            textLiteralWithExtent.Path = IfcTextPath.LEFT;
            textLiteralWithExtent.Extent = planarExtent;
            textLiteralWithExtent.BoxAlignment = new IfcBoxAlignment("bottom-left");

            IfcStyledItem styledItem = modelStore.Instances.New<IfcStyledItem>(item =>
            {
                item.Item = textLiteralWithExtent;
                item.Styles.Add(styleAssigment);
            });

            return textLiteralWithExtent;
        }
    }
}
