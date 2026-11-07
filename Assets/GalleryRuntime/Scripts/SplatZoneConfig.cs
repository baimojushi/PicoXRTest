using System;
using UnityEngine;

namespace GalleryRuntime
{
    [Serializable]
    public struct JsonVec3
    {
        public float x;
        public float y;
        public float z;

        public Vector3 ToVector3() => new Vector3(x, y, z);
    }

    [Serializable]
    public class SplatZoneDefinition
    {
        public bool enabled = true;
        public string id;
        public JsonVec3 center;
        /// <summary>热点方体尺寸（轴对齐，米）。由挂画识别管线写入；留空则编辑器用默认尺寸。</summary>
        public JsonVec3 boxSize;
        public float prefetchDistance = 6f;
        public float activateDistance = 4f;
        public float deactivateDistance = 6f;
        public float unloadDistance = 8f;
        public string lowPath;
        public string highPath;
        public string note;
    }

    [Serializable]
    public class SplatZoneManifest
    {
        public string globalBasePath;
        public SplatZoneDefinition[] zones;
    }
}
