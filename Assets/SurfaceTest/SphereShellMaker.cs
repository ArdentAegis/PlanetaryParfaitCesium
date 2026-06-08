using CesiumForUnity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace TerrainEngine {
    public class SphereShellMaker : MonoBehaviour
    {
        // Length and Width (in vertices) of the shell. 
        public int length = 100;
        public int width = 200;
        public double surfaceHeight = 10;
        public Material material;
        public GameObject Instance;
        // Stuff for creating the mesh.
        // Vertices, normals, and UVs will be indexed by (length, width)
        private Vector3[,] vertices;
        private Vector3[,] normals;
        private Vector2[,] uvs;
        int[,,] triangles;


        private JMARSScene scene;
        private CesiumEllipsoid ellipsoid;
        private GameObject surface;
        [SerializeField] public CesiumGeoreference GeoRef;
        //[SerializeField] public Vector3 GeoRefPosition;

        const float flatteningFactor = (1738100.0f-1736000.0f)/1738100.0f;

        private string[] bllonlat;
        private double startlon;
        private double startlat;

        private string[] trlonlat;
        private double endlon;
        private double endlat;
        
        private double londist;
        private double latdist;

        public double2 cesiumStartingLonLat;

        public Vector3 GetSpherePosition(double lon, double lat)
        {
            if (lon < -180) { lon = 360 + lon; }

            // Geodetic to Geocentric latitude correction
            lat = (double)(180 / Math.PI * MathF.Atan((1-flatteningFactor)*(1-flatteningFactor) * (float)Math.Tan((double)(lat * Math.PI / 180))));

            double3 lonlath = new double3(lon, lat, surfaceHeight);
            double3 ecef = GeoRef.ellipsoid.LongitudeLatitudeHeightToCenteredFixed(lonlath);
            double3 d3pos = GeoRef.TransformEarthCenteredEarthFixedPositionToUnity(ecef);
            Vector3 v3pos = new Vector3((float)d3pos.x, (float)d3pos.y, (float)d3pos.z);
            return v3pos;
        }

        public double2 PixelCoordinatesToLonLat(double x, double y, Texture2D heightTexture)
        {
            float u = (float)x / heightTexture.width;
            float v = (float)y / heightTexture.height;

            double lon = (double)(u * londist) + startlon;
            double lat = (double)((1-v) * latdist) + startlat;

            return new double2(lon, lat);
        }

        private void SetCoordinatesVariables()
        {
            bllonlat = scene.bottom_left.Split(", ");
            startlon = Convert.ToDouble(bllonlat[0]);
            startlat = Convert.ToDouble(bllonlat[1]);

            trlonlat = scene.top_right.Split(", ");
            endlon = Convert.ToDouble(trlonlat[0]);
            endlat = Convert.ToDouble(trlonlat[1]);
            
            londist = endlon - startlon;
            if (startlon > endlon) {
                londist += 360;
            }
            latdist = endlat - startlat;
            if (startlat > endlat)
            {
                latdist += 180;
            }
        }

        private void MakeVertices()
        {
            // Initialize the list of vertices, normals, and uvs (letting Unity figure out normals for now)
            vertices = new Vector3[length, width];
            //normals = new Vector3[length, width];
            uvs = new Vector2[length, width];

            // Coordinates! 
            SetCoordinatesVariables();

            //Debug.Log("Latitude: (" + startlat + ", " + endlat + ")");
            //Debug.Log("Longitude: (" + startlon + ", " + endlon + ")");

            // Length - 1 by width - 1 squares between vertices. 
            // Two triangles for each square
            // Three verties for each triangle
            triangles = new int[length - 1, width - 1, 2 * 3];

            for (int i = 0; i < length; i++) 
            {
                // Interpolating between the start and end latitudes of the scene
                double lat = (startlat + (i / (length - 1d)) * (latdist) % 180);
                for (int j = 0; j < width; j++)
                {
                    // Interpolating between the start and end longitudes of the scene (this gets calculated length times. Optimize with dynamic programming later)
                    double lon = (startlon + (j / (width - 1d)) * (londist))%360;
                    //Debug.Log(lat + ", " + lon);
                    // Get the position on the sphere for the vertex
                    vertices[i, j] = GetSpherePosition(lon, lat);
                    
                    // Get the vector pointing outwards from the center of the sphere (Letting Unity figure out normals right now)
                    //normals[i, j] = (vertices[i,j] - GeoRefPosition).normalized;

                    // Set the uv of this vertex (the -1s here are so that the edge vertices have a uv of 1)
                    uvs[i, j] = new Vector2(j/(width - 1f), i / (length - 1f));
                    
                    // Triangles is a list of indices. 
                    if (i < length - 1 && j < width - 1)
                    {
                        triangles[i, j, 0] = i * length + j;
                        triangles[i, j, 1] = (i + 1) * length + j;
                        triangles[i, j, 2] = i * length + j + 1;
                        
                        
                        triangles[i, j, 3] = i * length + j + 1;
                        triangles[i, j, 4] = (i + 1) * length + j;
                        triangles[i, j, 5] = (i + 1) * length + j + 1;
                       
                    }
                }
            }
        }
        // This should be called every time a scene is loaded.
        public void MakeSurface() 
        {
            // Set Cesium variables to use later
            scene = SceneMaterializer.singleton.selectedScene;
            ellipsoid = GeoRef.ellipsoid;


            // This code is duplicated in CesiumCornerPlacer!!
            double lat = Convert.ToDouble(scene.scene_center_lat);
            double lon = Convert.ToDouble(scene.scene_center_lon) * -1;
            if (lon < -180) { lon = 360 + lon; }
            cesiumStartingLonLat = new double2(lon, lat);
            GeoRef.SetOriginLongitudeLatitudeHeight(lon, lat, 0);

            //Instantiate(Instance, GetSpherePosition(lon, lat), Quaternion.identity, this.transform);
            //Instantiate(Instance, GetSpherePosition(startlon, startlat), Quaternion.identity, this.transform);
            MakeVertices();
        
            Destroy(surface);
            // Make the mesh that will be assigned to the new gameobject
            Mesh mesh = new Mesh {name = "Mesh Name" };
            // Flatten the arrays
            mesh.SetVertices(vertices.Cast<Vector3>().ToArray());

            // Letting Unity Figure out the normals for now. 
            //mesh.normals = normals.Cast<Vector3>().ToArray();
            //Vector3[] norm = new Vector3[length * width];
            //for (int i = 0; i < norm.Length; i++) { norm[i] = Vector3.up; }
            //mesh.normals = norm;

            mesh.SetUVs(0, uvs.Cast<Vector2>().ToArray());
            mesh.triangles = triangles.Cast<int>().ToArray();

            // Recalculate stuff. 
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            GameObject obj = new GameObject("hi :3");
            obj.transform.parent = this.transform;
            //obj.transform.localRotation = Quaternion.identity;
            obj.AddComponent<MeshFilter>();
            obj.AddComponent<MeshRenderer>();
            
            // Anchoring the mesh to the globe so they rotate together (?)
            //obj.AddComponent<CesiumGlobeAnchor>();

            obj.GetComponent<MeshFilter>().mesh = mesh;
            obj.GetComponent<MeshRenderer>().material = material;
            surface = obj;
        

        //DEBUG: Instantiates points at the vertices.
        //    for (int i = 0; i < vertices.GetLength(0); i++)
        //    {
        //        for (int j = 0; j < vertices.GetLength(1); j++)
        //        {
        //            var obj = Instantiate(Instance, Vector3.zero, Quaternion.identity, this.transform);
        //            obj.transform.SetPositionAndRotation(vertices[i, j], Quaternion.identity);
        //            obj.name = i + ", " + j;
        //        }
        //    }
        }

        private void Awake()
        {
            //MakeSurface();
        }
        // Start is called before the first frame update
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {

        }
    }
}