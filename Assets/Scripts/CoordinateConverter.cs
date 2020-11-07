using UnityEngine;

namespace Assets.Scripts
{
    public class CoordinateConverter
    {
        public float ScaleFactor { get; }

        private Matrix4x4 _coordinateConverter;

        // XvT engine -> Unity engine
        // unity: forward is +z, right is +x,    up is +y
        // XvT:   forward is -y, right is +x(?), up is +z
        private static readonly Matrix4x4 _baseConversionMatrix = new Matrix4x4(
            new Vector4(1, 0, 0, 0),
            new Vector4(0, 0, -1, 0),
            new Vector4(0, 1, 0, 0),
            new Vector4(0, 0, 0, 1));

        public CoordinateConverter(float scaleFactor)
        {
            ScaleFactor = scaleFactor;
            _coordinateConverter = _baseConversionMatrix * Matrix4x4.Scale(new Vector3(ScaleFactor, ScaleFactor, ScaleFactor));
        }

        /// <summary>
        /// Converts coordinates and scales them to the appropriate scaling factor
        /// </summary>
        public Vector3 ConvertCoordinates(Vector3 point) => _coordinateConverter.MultiplyPoint3x4(point);
    }
 }