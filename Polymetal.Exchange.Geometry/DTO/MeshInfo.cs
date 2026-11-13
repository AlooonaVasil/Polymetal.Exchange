using System.Collections.Generic;
using Xbim.Ifc;
using Xbim.Ifc2x3.Interfaces;

namespace Polymetal.Exchange.Geometry.DTO
{
    internal class MeshInfo
    {
        internal int GeometryId;
        internal List<int> FaceIndexes;
        internal IIfcSurfaceStyle SurfaceStyle;
        internal XbimTexture _texture;

        internal XbimTexture Texture
        {
            get => _texture;
            set => _texture = value;
        }

        private XbimTexture _defaultTexture = XbimTexture.Create(new XbimColour("def", 1, 1, 1));

        internal XbimTexture DefaultTexture => _defaultTexture;

        internal bool IsReserveGeometry =>
            SurfaceStyle?.Name != null && SurfaceStyle.Name.Value.Equals("ПМИ_Зона_Резервирования");

        internal MeshInfo()
        {
            FaceIndexes = new List<int>();
        }
    }
}
