using CesiumForUnity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using System.IO;

namespace TerrainEngine {
    public class CesiumJMARSTerrainMaker : MonoBehaviour
    {
        // Length and Width (in vertices) of the shell. 
        public int length = 100;
        public int width = 100;
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

        // used in conversion between Geodetic and Geocentric coordinates
        // (equatorial radius - polar radius) / equatorial radius
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
        public Vector3 georeferenceStartingPosition = new Vector3(0, -5f, 0);

        float elapsedTime = 0;
        Mesh mesh;

        [Header("Height Error Coloring")]
        // click in inspector to run ShowErrorColorCesium() once
        [Tooltip("Click to run ShowErrorColorCesium() once")]
        [SerializeField] private bool generateErrorColor = false;
        // toggle in inspector to use Unity world positions for distance comparisons
        [Tooltip("Toggle to use Unity world positions for distance comparisons")]
        [SerializeField] private bool compareUnityDistance;
        // toggle in inspector to use Cesium height values and JMARS height texture values for distance comparisons
        [Tooltip("Toggle to use Cesium height values and JMARS height texture values for distance comparisons")]
        [SerializeField] private bool compareHeightMapDistance;
        // toggle in inspector to make distance error coloring visible
        [Tooltip("Toggle to make distance error coloring visible")]
        [SerializeField] private bool toggleErrorColor;

        [Header("Terrain Superimposing")]
        [Tooltip("Click to run PlaceTerrain() once")]
        [SerializeField] private bool placeTerrain;

        void Start()
        {
            generateErrorColor = false;
            compareUnityDistance = false;
            compareHeightMapDistance = true;
            toggleErrorColor = false;

            placeTerrain = false;
        }

        public void Update()
        {
            if (SceneMaterializer.singleton.useCesium)
            {
                // press ready button in Unity inspector to color mesh 
                if (generateErrorColor)
                {
                    ShowErrorColorCesium();
                    generateErrorColor = false;
                }

                if (placeTerrain)
                {
                    PlaceTerrain();
                    placeTerrain = false;
                }

                SceneMaterializer.singleton.activeMaterial.SetInt("_showErrorColor", toggleErrorColor ? 1 : 0);

            }
        }

        /// <summary>
        /// Creates and places JMARS terrain onto Cesium mesh
        /// </summary>
        public IEnumerator MakeSurface() 
        {
            // destroys any previous terrain meshes
            Destroy(surface);

            // set Cesium variables to use later
            scene = SceneMaterializer.singleton.selectedScene;
            ellipsoid = GeoRef.ellipsoid;

            // gets scene center coordinates
            double lat = Convert.ToDouble(scene.scene_center_lat);
            double lon = Convert.ToDouble(scene.scene_center_lon) * -1; // * -1 to match Cesium coordinate system
            // ensures longitude is between -180 and 180 
            if (lon < -180) { lon = 360 + lon; }

            // sets starting coordinates for reseting position during run-time
            cesiumStartingLonLat = new double2(lon, lat);
            // makes starting coordinates start directly in center of the game world
            GeoRef.SetOriginLongitudeLatitudeHeight(lon, lat, 0);

            
            // makes the vertices of the JMARS terrain mesh
            yield return StartCoroutine(MakeVertices());
        
            // make the mesh that will be assigned to the new gameobject
            mesh = new Mesh {name = "Terrain Shell"};
            // flatten the 2D arrays into 1D arrays
            mesh.SetVertices(vertices.Cast<Vector3>().ToArray());
            mesh.SetUVs(0, uvs.Cast<Vector2>().ToArray());
            mesh.triangles = triangles.Cast<int>().ToArray();

            // recalculate mesh variables 
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            // creates and places terrain gameobject
            GameObject obj = new GameObject("Terrain Mesh");
            obj.transform.parent = this.transform;
            obj.transform.position = this.transform.position;

            // applies exaggeration from JMARS height texture and current exaggeration slider value to position
            // float heightValue = (transform.localScale.y / 200f) * 
            //                     scene.depthTexture.GetPixel((int)(scene.depthTexture.width / 2),
            //                                                 (int)(scene.depthTexture.height / 2)).r * 0.001f; // 0.001 is moon scale
            // // moves position based on height texture value
            // Vector3 position = this.transform.position;
            // position.y -= heightValue;
            // transform.position = position;

            transform.localRotation = Quaternion.identity;
            obj.transform.localRotation = Quaternion.identity;
            obj.AddComponent<MeshFilter>();
            obj.AddComponent<MeshRenderer>();

            // applies mesh and material to terrain gameobject
            obj.GetComponent<MeshFilter>().mesh = mesh;
            obj.GetComponent<MeshRenderer>().material = material;

            // sets surface to new terrain gameobject
            surface = obj;

            PlaceTerrain();
        

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

        /// <summary>
        /// Gets Unity world position on Cesium ellipsoid from JMARS coordinates 
        /// </summary>
        /// <param name="lon"></param> Longitude coordinate
        /// <param name="lat"></param> Latitude coordinate, in Geodetic coordinate system
        public Vector3 GetSpherePosition(double lon, double lat)
        {
            // converts longitude to within -180 to 180 range
            if (lon < -180) { lon = 360 + lon; }

            // Geodetic to Geocentric latitude correction
            lat = (double)(Mathf.Rad2Deg * Mathf.Atan((1-flatteningFactor)*(1-flatteningFactor) * (float)Math.Tan((double)(lat * Mathf.Deg2Rad))));

            // gets world position using Cesium functions
            double3 lonlath = new double3(lon, lat, surfaceHeight);
            // convert longitude, latitude, and height to Ellipsoid-Centered, Ellipsoid-Fixed (ECEF) coordinates
            double3 ecef = GeoRef.ellipsoid.LongitudeLatitudeHeightToCenteredFixed(lonlath);
            // transforms ECEF position to Unity world position
            double3 d3pos = GeoRef.TransformEarthCenteredEarthFixedPositionToUnity(ecef);
            Vector3 v3pos = new Vector3((float)d3pos.x, (float)d3pos.y, (float)d3pos.z);
            return v3pos;
        }

        /// <summary>
        /// Gets Lat/Lon coordinates of a pixel at (x, y) on JMARS texture
        /// </summary>
        /// <param name="x"></param> Pixel's x position
        /// <param name="y"></param> Pixel's y position
        /// <param name="heightTexture"></param> Height texture from JMARS terrain
        public double2 PixelCoordinatesToLonLat(double x, double y, Texture2D heightTexture)
        {
            // normalizes (x, y) coordinates to [0, 1] range
            float u = (float)x / heightTexture.width;
            float v = (float)y / heightTexture.height;

            // linearly interpolates between start and end coordinates of JMARS terrain
            double lon = (double)(u * londist) + startlon;
            double lat = (double)((1-v) * latdist) + startlat;

            return new double2(lon, lat);
        }
        
        /// <summary>
        /// Sets necessary coordinates variables from JMARS terrain .json for use later
        /// </summary>
        private void SetCoordinatesVariables()
        {
            // coordinates of bottom left corner of terain
            bllonlat = scene.bottom_left.Split(", ");
            startlon = Convert.ToDouble(bllonlat[0]);
            startlat = Convert.ToDouble(bllonlat[1]);

            // coordinates of top right corner of terrain
            trlonlat = scene.top_right.Split(", ");
            endlon = Convert.ToDouble(trlonlat[0]);
            endlat = Convert.ToDouble(trlonlat[1]);
            
            // difference between start and end lon used for linear interpolation
            londist = endlon - startlon;
            // ensures difference is positive
            if (startlon > endlon) {
                londist += 360;
            }

            // difference between start and end lat used for linear interpolation
            latdist = endlat - startlat;
            // ensures difference is positive
            if (startlat > endlat)
            {
                latdist += 180;
            }
        }

        /// <summary>
        /// Creates vertices of JMARS terrain mesh
        /// </summary>
        private IEnumerator MakeVertices()
        {
            // Initialize the list of vertices, normals, and uvs (letting Unity figure out normals for now)
            vertices = new Vector3[length, width];
            //normals = new Vector3[length, width];
            uvs = new Vector2[length, width];

            // sets needed coordinates variables
            SetCoordinatesVariables();

            // gets surface height to place JMARS terrain at proper height
            yield return StartCoroutine(FindSurfaceHeight());

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
                    // Get the Unity world position on the sphere for the vertex
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

        /// <summary>
        /// Raycasts down from center of JMARS terrain onto Cesium mesh to get surface height, which is used to place JMARS terrain at proper height
        /// </summary>
        public IEnumerator FindSurfaceHeight()
        {
            // waits long enough for Cesium terrain to load
            yield return new WaitForSeconds(3.0f);

            // raycasts down from center of JMARS terrain onto Cesium mesh
            RaycastHit hit;
            LayerMask layer = LayerMask.GetMask("Default");
			if (Physics.Raycast(Vector3.zero, Vector3.down, out hit, 1000f, layer)) {
                // Georeference assumes that it is at the orgin, so adjust for Georeference gameobject's position
                Vector3 relPosition = hit.point - GeoRef.transform.position;
                // gets Ellipsoid-Centered, Ellipsoid-Fixed (ECEF) coordinates of raycast hit point
                double3 ecef = GeoRef.TransformUnityPositionToEarthCenteredEarthFixed(new double3(relPosition.x, relPosition.y, relPosition.z));
                // sets surface height to height of ECEF coordinate
                surfaceHeight = (float)GeoRef.ellipsoid.CenteredFixedToLongitudeLatitudeHeight(ecef).z;
            }
        }

        /// <summary>
        /// Colors JMARS terrain texture based on terrain mesh's vertices' distance from Cesium mesh
        /// </summary>
        public void ShowErrorColorCesium()
        {
            // gets terrain mesh information
            Vector3[] verts = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            float[] distances = new float[verts.Length];
            Color[] colors = new Color[verts.Length];

            // initializes max distance
            float maxdistance = 0;

            string x = "";
            string y = "";

            // loops through each vertex of the mesh
            for (int i = 0; i < verts.Length - width /*skips last row of vertices*/; i++)
            {
                // does not check edges of terrain where height map data is inaccurate
                if (i % length == length - 1)
                {
                    continue;
                }

                // gets the world position of the current vertex, corrected for the terrain's scaling of (200, 200, 200) 
                Vector3 worldVertexPos = transform.TransformPoint(verts[i] / 200.0f);

                // // applies exaggeration from JMARS height texture and current exaggeration slider value to vertex position
                // float heightValue = (transform.localScale.y / 200.0f) * scene.depthTexture.GetPixel((int)(scene.depthTexture.width * 
                //                     uvs[i].x), (int)(scene.depthTexture.height * uvs[i].y)).r * 0.001f; // 0.001 is moon scale
                // // offsets position based on height texture value
                // worldVertexPos.y += heightValue;
                
                // offsets position up to ensure raycast collides with Cesium mesh
                Vector3 offset = new Vector3(0, 1.0f, 0);

                GameObject origin = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(origin.GetComponent<BoxCollider>());
                origin.transform.localScale = origin.transform.localScale * 0.3f;
                origin.transform.position = worldVertexPos;
                origin.name = i.ToString();

                GameObject test = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                test.SetActive(false);
                test.transform.localScale = test.transform.localScale * 0.3f;
                Destroy(test.GetComponent<SphereCollider>());
                    
                // raycast up and down to find Cesium mesh
                RaycastHit hit;
                LayerMask layerMask = LayerMask.GetMask("Default");
                
                // raycast down
                if (Physics.Raycast(worldVertexPos + offset,
                                    //Vector3.Normalize(worldVertexPos - (GeoRef.transform.position + new Vector3(0, -1737.4f, 0))), 
                                    -Vector3.up,
                                    out hit, 50f, layerMask))
                {
                    Vector3 position = hit.point;
                    // Georeference assumes that it is at the orgin, so adjust for Georeference gameobject's position
                    Vector3 relPosition = position - GeoRef.transform.position;
                    
                    double3 ecef = GeoRef.TransformUnityPositionToEarthCenteredEarthFixed(new double3((double)relPosition.x, (double)relPosition.y, (double)relPosition.z));
                    double3 lonlath = GeoRef.ellipsoid.CenteredFixedToLongitudeLatitudeHeight(ecef);

                    x += lonlath.z.ToString() + "\n";
                    y += scene.depthTexture.GetPixel((int)(scene.depthTexture.width * uvs[i].x), (int)(scene.depthTexture.height * uvs[i].y)).r.ToString() + "\n";
                    
                    // gets distance from vertex point to Cesium mesh and saves it
                    float distance = 0.0f;
                    // compares Unity world positions between meshes
                    if (compareUnityDistance) distance = Vector3.Magnitude(hit.point - worldVertexPos);
                    // compares height values from Cesium Moon and JMARS height texture
                    else if (compareHeightMapDistance) distance = (float)lonlath.z - scene.depthTexture.GetPixel((int)(scene.depthTexture.width * uvs[i].x), (int)(scene.depthTexture.height * uvs[i].y)).r;
                    distances[i] = distance; 

                    test.transform.position = hit.point;
                    test.name = distance.ToString();
                    
                    if (distance > maxdistance) maxdistance = (float)distance;
                }
                // raycast up
                else if (Physics.Raycast(worldVertexPos + offset,
                                    //Vector3.Normalize((GeoRef.transform.position + new Vector3(0, -1737.4f, 0)) - worldVertexPos), 
                                    Vector3.up,
                                    out hit, 50.0f, layerMask))
                {
                    Vector3 position = hit.point;
                    // Georeference assumes that it is at the orgin, so adjust for Georeference gameobject's position
                    Vector3 relPosition = position - GeoRef.transform.position;
                    
                    double3 ecef = GeoRef.TransformUnityPositionToEarthCenteredEarthFixed(new double3((double)relPosition.x, (double)relPosition.y, (double)relPosition.z));
                    double3 lonlath = GeoRef.ellipsoid.CenteredFixedToLongitudeLatitudeHeight(ecef);

                    // gets distance from vertex point to Cesium mesh and saves it
                    float distance = 0.0f;
                    // compares Unity world positions between meshes
                    if (compareUnityDistance) distance = Vector3.Magnitude(hit.point - worldVertexPos);
                    // compares height values from Cesium Moon and JMARS height texture
                    else if (compareHeightMapDistance) distance = (float)lonlath.z - scene.depthTexture.GetPixel((int)(scene.depthTexture.width * uvs[i].x), (int)(scene.depthTexture.height * uvs[i].y)).r;
                    distances[i] = distance; 

                    test.transform.position = hit.point;
                    test.name = distance.ToString();
                    
                    if (distance > maxdistance) maxdistance = (float)distance;
                }
                else Debug.Log("No Raycast Hit");
            }

            Debug.Log(maxdistance);

            // set vertex color based on distance from Cesium mesh
            for (int i = 0; i < vertices.Length; i++)
            {
                colors[i] = new Color(distances[i] / maxdistance, 1 - distances[i] / maxdistance, 0, 1);
            }

            // assign colors back to the mesh
            mesh.SetColors(colors);

            string path = Path.Combine(Application.persistentDataPath, "x.txt");
            File.WriteAllText(path, x);
            string path2 = Path.Combine(Application.persistentDataPath, "y.txt");
            File.WriteAllText(path2, y);

        }

        private void PlaceTerrain()
        {
            // gets terrain mesh information
            Vector3[] verts = mesh.vertices;
            Vector2[] uvs = mesh.uv;

            // loops through each vertex of the mesh
            for (int i = 0; i < verts.Length; i++)
            {
                // gets the world position of the current vertex, corrected for the terrain's scaling of (200, 200, 200) 
                Vector3 worldVertexPos = transform.TransformPoint(verts[i] / 200.0f);
                
                // offsets position up to ensure raycast collides with Cesium mesh
                Vector3 offset = new Vector3(0, 10.0f, 0);

                // GameObject origin = GameObject.CreatePrimitive(PrimitiveType.Cube);
                // Destroy(origin.GetComponent<BoxCollider>());
                // origin.transform.localScale = origin.transform.localScale * 0.3f;
                // origin.transform.position = worldVertexPos;
                // origin.name = i.ToString();

                // GameObject test = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                // test.SetActive(true);
                // test.transform.localScale = test.transform.localScale * 0.1f;
                // Destroy(test.GetComponent<SphereCollider>());
                    
                // raycast up and down to find Cesium mesh
                RaycastHit hit;
                LayerMask layerMask = LayerMask.GetMask("Default");
                
                // raycast down
                if (Physics.Raycast(worldVertexPos + offset,
                                    //Vector3.Normalize(worldVertexPos - (GeoRef.transform.position + new Vector3(0, -1737.4f, 0))), 
                                    -Vector3.up,
                                    out hit, 50f, layerMask))
                {
                    Vector3 position = hit.point;
                    verts[i] = hit.point - GeoRef.transform.position;
                    verts[i].y += 0.05f;

                    // test.transform.position = hit.point;
                }
                // raycast up
                else if (Physics.Raycast(worldVertexPos + offset,
                                    //Vector3.Normalize((GeoRef.transform.position + new Vector3(0, -1737.4f, 0)) - worldVertexPos), 
                                    Vector3.up,
                                    out hit, 50.0f, layerMask))
                {
                    Vector3 position = hit.point;
                    verts[i] = hit.point - GeoRef.transform.position;
                    verts[i].y += 0.05f;

                    // test.transform.position = hit.point;
                }
                else Debug.Log("No Raycast Hit");
            }

            mesh.SetVertices(verts);
        }
    }
}