using UnityEngine;
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

namespace VoxelSystem.Demo {

    [RequireComponent (typeof(MeshFilter))]
    public class CPUDemo : MonoBehaviour {

		[SerializeField] protected Mesh mesh;
        [Tooltip("Smaller means large voxels, 3 is very crud, 8 is minecraft, 15 is half-minecraft, 40 is is good enought for sub-voxels, 120 is almost indistinguishable from mesh (losing holes)")]
        [SerializeField] protected int resolution = 24;
        [SerializeField] protected bool useUV = false;

        [SerializeField] private bool Generate = false;
        
        private void OnValidate()
        {
            if (Generate)
            {
                Generate = false;
                GenerateMesh();
            }
        }

        void GenerateMesh () {
            List<Voxel_t> voxels;
            float unit;
            CPUVoxelizer.Voxelize(mesh, resolution, out voxels, out unit);

            var filter = GetComponent<MeshFilter>();
            filter.sharedMesh = VoxelMesh.Build(voxels.ToArray(), unit, useUV);
        }

    }

}


