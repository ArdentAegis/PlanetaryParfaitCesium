using Multiuser.Sync;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using UserInterface;
using XRController = XRControls.XRController;
using Unity.Mathematics;
using CesiumForUnity;

namespace TerrainEngine.Tools
{
    /// <summary>
    /// Reads numeric data by the pixel on the terrain. 
    /// </summary>
    public class PerPixelDataReader : MonoBehaviour
    {
        #region FIELDS

        public static PerPixelDataReader singleton;

        /// <summary>
        /// True if per-pixel tool is enabled, false otherwise.
        /// </summary>
        public bool readingData = false;


        [SerializeField] public CesiumGeoreference GeoRef;
        [SerializeField] public Transform check;
        [SerializeField] public float surfaceHeight;
        // Pins
        public GameObject pins;
        public static List<Pin> pinList;
        [SerializeField] private GameObject pinInfoPrefab;
        [SerializeField] private GameObject pinPrefab;
        [SerializeField] private GameObject platform;

        // Terrain variables
        public GameObject terrain; // Tiles object
        public Material material;
        private Texture2D heightTexture;

        // Terrain Intersection Variables
        private Ray ray;

        /// <summary>
        /// Determines if the ray has made an intersection with the terrain. +1 if positive, -1 if negative
        /// </summary>
        private int lastSign = 0;

        // VR-related Variables
        [SerializeField] private XRController controlCheck; // determines if we are pressing the button in VR
        [SerializeField] private GameObject vrController;
        private bool printOnce; // ensures that a Pin is spawned into a scene once. Prevents scale bar from spawning every frame in VR



        #region DATA

        public static List<float[]> floatArrays = new List<float[]>();   //list of float arrays containing per pixel data
        public static List<short[]> shortArrays = new List<short[]>();  //list of short arrays containing per pixel data
        public static List<int[]> intArrays = new List<int[]>();     //list of int arrays containing per pixel data
        public static List<byte[]> byteArrays = new List<byte[]>();  //list of byte arrays containing per pixel data

        public static List<string> floatDataUnits = new List<string>();
        public static List<string> floatDataNames = new List<string>();
        public static List<string> shortDataUnits = new List<string>();
        public static List<string> shortDataNames = new List<string>();

        public static List<string> intDataUnits = new List<string>();
        public static List<string> intDataNames = new List<string>();
        public static List<string> byteDataUnits = new List<string>();
        public static List<string> byteDataNames = new List<string>();

        public static string floatOutput = "";
        public static string intOutput = "";
        public static string byteOutput = "";
        public static string shortOutput = "";
        private string imagePosition = "";

        #endregion

        private int framec = 0;
        private int framem = 200;
        #endregion

        #region MONO
        void Start()
        {
            singleton = this;
            pinList = new List<Pin>();

            if (GameState.IsVR)
            {
                controlCheck = GameObject.FindGameObjectWithTag("Player").GetComponent<XRController>();
            }

            heightTexture = material.GetTexture("_HeightMap") as Texture2D;
        }

        private void FixedUpdate()
        {
            framec++;
            if (pinList.Count != 0)
            {
                //Ensure Pin Info follows the pins
                for (int i = 0; i < pinList.Count; i++)
                {
                    //ensures pins follow terrain, even if exaggeration changes
                    pinList[i].pin.transform.position = terrain.transform.TransformPoint(pinList[i].position);
                    pinList[i].panel.transform.position = new Vector3(pinList[i].pin.transform.position.x,
                        pinList[i].pin.transform.position.y + 4.5f,
                        pinList[i].pin.transform.position.z);
                    pinList[i].panel.transform.localScale = new Vector3(-1, 1, 1);
                    pinList[i].panel.transform.LookAt(platform.transform); // rotate to platform so it always faces user

                }
            }

            //Per Pixel Data Button in scene; user has Per Pixel tool enabled if readingData=true
            if (readingData)
            {
                //check which rig is used so we can raycast properly
                if (!GameState.IsVR) //desktop
                {
                    Vector3 mousePos = Input.mousePosition;
                    //ray.origin = Camera.main.ScreenToWorldPoint(mousePos);

                    if (Cursor.lockState == CursorLockMode.Locked)
                    {
                        ray.origin = Camera.main.transform.position;
                        ray.direction = Camera.main.transform.forward;
                    }
                    else { 
                        ray = Camera.main.ScreenPointToRay(mousePos);
                    }
                }
                else if (GameState.IsVR) //VR
                {
                    ray.origin = vrController.transform.position;
                    ray.direction = vrController.transform.forward;
                }
                else
                {
                    Debug.LogError("rig error");
                }

                //Dynamic readout
                if (SceneDownloader.singleton.dataLayers.Count == 0) // no layers
                {
                    // Set text -- change this so it doesn't happen every frame.
                    TerrainTools.DynamicReadout("There are no data layers associated with this terrain.", "");
                }
                else // existing layers
                {
                    // Set text & calculate user's raycast to terrain
                    TerrainTools.DynamicReadout("Per Pixel Data", imagePosition + floatOutput + shortOutput + intOutput + byteOutput);
                    CalculateRay();
                }

            }

            //makes sure that pins are ONLY being printed once when a user holds down controller grip
            if (controlCheck.triggerActive == false) printOnce = false;
        }
        #endregion

        #region METHODS

        public void CalculateRay()
        {
            float step = 0.01f;
            lastSign = 0;

            //resetting data outputs
            floatOutput = "";
            shortOutput = "";
            intOutput = "";
            byteOutput = "";

            heightTexture = material.GetTexture("_HeightMap") as Texture2D;
            JMARSScene scene = SceneMaterializer.singleton.selectedScene;

            string[] bllonlat = scene.bottom_left.Split(", ");
            float startlon = Convert.ToSingle(bllonlat[0]);
            // Convert from 0 -> 360 to -180 -> 180
            if (startlon > 180)
            {
                startlon -= 360;
            }
            float startlat = Convert.ToSingle(bllonlat[1]);

            string[] trlonlat = scene.top_right.Split(", ");
            float endlon = Convert.ToSingle(trlonlat[0]);
            // Convert from 0 -> 360 to -180 -> 180
            if (endlon > 180)
            {
                endlon -= 360;
            }
            float endlat = Convert.ToSingle(trlonlat[1]);

            float scaleFactor = material.GetFloat("_scaleFactor");
            
            lastSign = 0;
            //1000 is an arbitary big number (we know that the user will not be THAT far away from the terrain)
            //this for-loop steps from the starting point (player location & camera direction) and the intersection point (val_intersection)
            //we are looking for val_intersection
            for (float t = 0.01f; t < 100; t += step)
            {
                Vector3 position = ray.origin + t * ray.direction;
                
                // Georeference assumes that it is at the orgin.
                Vector3 relPosition = position - GeoRef.transform.position;
                
                double3 ecef = GeoRef.TransformUnityPositionToEarthCenteredEarthFixed(new double3((double)relPosition.x, (double)relPosition.y, (double)relPosition.z));
                double3 lonlath = GeoRef.ellipsoid.CenteredFixedToLongitudeLatitudeHeight(ecef);

                float cesiumHeight = (float)lonlath.z;
                //float realCesiumHeight = cesiumHeight * (float)GeoRef.scale;
                
                
                float u = ((float)lonlath.x - startlon) / (endlon - startlon);
                float v = ((float)lonlath.y - startlat) / (endlat - startlat);


                float jmarsHeight = heightTexture.GetPixelBilinear(u, v).r;

                float visualheight = jmarsHeight * scaleFactor * .00001f * (float)(1d / GeoRef.scale);

                float heightdiff = visualheight - cesiumHeight;


                if (lastSign == 0)
                {
                    lastSign = (int)Mathf.Sign(heightdiff);
                }

                // Detect Collision
                if ((Mathf.Abs(heightdiff) < 1 || lastSign == -((int)Mathf.Sign(heightdiff)) ) && !(u < 0 || u > 1 || v < 0 || v > 1))
                {
                    //if (GameState.printPerPixelCoordinates)   
                    if (framec >= framem)
                    {   
                        Debug.Log("(" + u + ", " + v + ")");
                        Debug.Log("JMARS: " + SceneDownloader.singleton.realHeight.GetPixelBilinear(u, v).r);
                        //Debug.Log("Cesium: " + cesiumHeight);
                        //Debug.Log("DIFF: " + heightdiff);
                        //Debug.Log("Dist: " + t);
                        Debug.Log("Lat : " + lonlath.y + ", Lon: " + lonlath.x);

                        //Debug.Log(GeoRef.ellipsoid.CenteredFixedToLongitudeLatitudeHeight(GeoRef.TransformUnityPositionToEarthCenteredEarthFixed(new double3((double)transform.position.x, (double)transform.position.y, (double)transform.position.z))));
                        framec = 0;
                    }
                    check.position = position;
                    int index = (int)MathF.Round(u * heightTexture.width + (1-v) * heightTexture.height);
                    #region DYNAMIC_READOUT
                    for (int i = 0; i < floatArrays.Count; i++)
                    {
                        if (index <= (heightTexture.width * heightTexture.height))
                        {
                            floatOutput += floatDataNames[i] + ": " + floatArrays[i][index] + " " + floatDataUnits[i] + "\n";
                        }
                        else
                        {
                            floatOutput = "";
                        }

                    }
                    for (int i = 0; i < intArrays.Count; i++)
                    {
                        if (index <= (heightTexture.width * heightTexture.height))
                        {
                            intOutput += intDataNames[i] + ": " + intArrays[i][index] + " " +
                                         intDataUnits[i] + "\n";
                        }
                        else
                        {
                            intOutput = "";
                        }
                    }
                    for (int i = 0; i < byteArrays.Count; i++)
                    {
                        if (index <= (heightTexture.width * heightTexture.height))
                        {
                            byteOutput += byteDataNames[i] + ": " + byteArrays[i][index] + " " +
                                          byteDataUnits[i] + "\n";
                        }
                        else
                        {
                            byteOutput = "";
                        }
                    }

                    for (int i = 0; i < shortArrays.Count; i++)
                    {
                        if (index <= (heightTexture.width * heightTexture.height))
                        {
                            shortOutput += shortDataNames[i] + ": " + shortArrays[i][index] + " " +
                                           shortDataUnits[i] + "\n";
                        }
                        else
                        {
                            shortOutput = "";
                        }
                    }
                    #endregion
                    string dataOutput = "";

                    // For debugging per-pixel data
                    int flippedZ = (int)Mathf.Round(heightTexture.height * (1-v)); //Used ONLY in print statements. Remember, the data is FLIPPED
                    if (GameState.printPerPixelCoordinates)
                    {
                        imagePosition = "Index = " + index + "\n" +
                                        "X Position = " + u * heightTexture.width + "\n" +
                                        "Z Position = " + flippedZ + "\n";
                        dataOutput = imagePosition + floatOutput + intOutput + byteOutput + shortOutput;
                    }
                    else
                    {
                        dataOutput = floatOutput + intOutput + byteOutput + shortOutput;
                    }

                    //placing pins
                    if (Input.GetMouseButtonDown(1) || (controlCheck.triggerActive && controlCheck.leftHandActive && !printOnce))
                    {
                        SpawnPin(position, dataOutput, SceneDownloader.singleton.guid);
                        printOnce = true;
                    }
                    break;
                }
            }
    } 

        /// <summary>
        /// Creates raycast from user's mouse/controller to the terrain.
        /// </summary>
        public void CalculateRayOLD()
        {
            float step = 0.025f;
            lastSign = 0;
            
            //resetting data outputs
            floatOutput = "";
            shortOutput = "";
            intOutput = "";
            byteOutput = "";

            //1000 is an arbitary big number (we know that the user will not be THAT far away from the terrain)
            //this for-loop steps from the starting point (player location & camera direction) and the intersection point (val_intersection)
            //we are looking for val_intersection
            for (float t = 0.01f; t < 1000; t += step)
            {
                //changing ray coordinates to terrain corrdiantes
                Vector3 ray_origin_terrain = terrain.transform.InverseTransformPoint(ray.origin);
                Vector3 ray_origin_terrain2 = terrain.transform.InverseTransformPoint(ray.origin + ray.direction);
                Vector3 ray_direction_terrain = ray_origin_terrain2 - ray_origin_terrain;
                Vector3 ray_t = ray_origin_terrain + (ray_direction_terrain * t); //t is the distance of the ray

                heightTexture = material.GetTexture("_HeightMap") as Texture2D;

                //out of bounds conditions
                if (ray_t.x > 0.5f || ray_t.x < -0.5f || ray_t.z > 0.5f || ray_t.z < -0.5f) return;

                //converts terrain coordinates to texture coordinates
                int x_pos_img = (int)(((ray_t.x + 0.5f) * heightTexture.width)); //0.5f gets the x value to its relative position in the Unity Scene
                int z_pos_img = (int)(((0.5f - ray_t.z) * heightTexture.height));   
                
                //gets heightvalue from depth texture at (x, z) texture coordinates 
                float heightValue = heightTexture.GetPixel(x_pos_img, z_pos_img).r; //height texture is stored in the red channel
                // -1 added because this is upside down somehow. 
                // Multiplied by 1e-5 to account for the factor added to the shell material shader. 
                //float h_t = heightValue * material.GetFloat("_scaleFactor") * -1f * .00001f; //scales heightvalue by current exag value

                float h_t = heightValue * material.GetFloat("_scaleFactor") * .00001f;
                float ht_minus_rty = (h_t - ray_t.y);
                
                //determines if (x, z) position is in bounds
                int index = Math.Abs(z_pos_img- heightTexture.height) * heightTexture.width + x_pos_img;
                
                //first time entering the loop
                if (lastSign == 0)
                {
                    lastSign = (int)Mathf.Sign(ht_minus_rty);
                }
                
                // found intersection
                if (ht_minus_rty == 0 || lastSign == -(Mathf.Sign(ht_minus_rty)))
                {
                    #region DYNAMIC_READOUT
                    for (int i = 0; i < floatArrays.Count; i++)
                    {
                        if (index <= (heightTexture.width * heightTexture.height))
                        {
                            floatOutput += floatDataNames[i] + ": " + floatArrays[i][index] + " " + floatDataUnits[i] + "\n";
                        }
                        else
                        {
                            floatOutput = "";
                        }

                    }
                    for (int i = 0; i < intArrays.Count; i++)
                    {
                        if (index <= (heightTexture.width * heightTexture.height))
                        {
                            intOutput += intDataNames[i] + ": " + intArrays[i][index] + " " +
                                         intDataUnits[i] + "\n";
                        }
                        else
                        {
                            intOutput = "";
                        }
                    }
                    for (int i = 0; i < byteArrays.Count; i++)
                    {
                        if (index <= (heightTexture.width * heightTexture.height))
                        {
                            byteOutput += byteDataNames[i] + ": " + byteArrays[i][index] + " " +
                                          byteDataUnits[i] + "\n";
                        }
                        else
                        {
                            byteOutput = "";
                        }
                    }

                    for (int i = 0; i < shortArrays.Count; i++)
                    {
                        if (index <= (heightTexture.width * heightTexture.height))
                        {
                            shortOutput += shortDataNames[i] + ": " + shortArrays[i][index] + " " +
                                           shortDataUnits[i] + "\n";
                        }
                        else
                        {
                            shortOutput = "";
                        }
                    }
                    #endregion

                    string dataOutput = "";
                    
                    // For debugging per-pixel data
                    int flippedZ = heightTexture.height - z_pos_img; //Used ONLY in print statements. Remember, the data is FLIPPED
                    if (GameState.printPerPixelCoordinates)
                    {
                        imagePosition = "Index = " + index  + "\n" +
                                        "X Position = " + x_pos_img + "\n" +
                                        "Z Position = " + flippedZ + "\n";
                        dataOutput = imagePosition + floatOutput + intOutput + byteOutput + shortOutput;
                    }
                    else  
                    {
                        dataOutput = floatOutput + intOutput + byteOutput + shortOutput;
                    }
                    
                    //placing pins
                    if (Input.GetMouseButtonDown(1) || (controlCheck.triggerActive && controlCheck.leftHandActive && !printOnce))
                    {
                        SpawnPin(ray_t, dataOutput, SceneDownloader.singleton.guid);
                        printOnce = true; 
                    }

                    break;
                }
            }

        }
        
        /// <summary>
        /// Spawns a Guest pin in Multiplayer.
        /// </summary>
        /// <param name="position">Pin position in world space coordinates</param>
        /// <param name="data">Pin data</param>
        /// <param name="guid">Guid for player that spawned the pin</param>
        public void SpawnPin(Vector3 position, string data, string guid)
        {
            if (GameState.InMultiuser)
            {
                if (NetworkManager.Singleton.IsHost)
                {
                    Pin _pin = CreatePin(position, data);
                    
                    _pin.guid = guid;
                    _pin.panel.GetComponent<PerPixelSync>().pinData.Value = data;
                    _pin.panel.GetComponent<PerPixelSync>().pinNumber.Value = (pinList.Count+1).ToString();
                    _pin.clientID = NetworkManager.Singleton.LocalClientId;
                    pinList.Add(_pin);
                    
                    _pin.pin.GetComponent<NetworkObject>().Spawn();
                    _pin.panel.GetComponent<NetworkObject>().Spawn();
                }
                else
                {
                    pins.GetComponent<PinRPCS>().PinServerRpc(position, data, guid);  
                }
            }
            else
            {
                pinList.Add(CreatePin(position, data));
            }
        }

        public Pin CreatePin(Vector3 position, string data)
        {
            GameObject debugSphere = Instantiate(pinPrefab);
            Vector3 debugSphereTransform = terrain.transform.TransformPoint(position);
            debugSphere.transform.localPosition = debugSphereTransform;
            debugSphere.transform.localScale = Vector3.one * 0.5f;

            GameObject newPinCanvas = Instantiate(pinInfoPrefab);
            newPinCanvas.transform.position = new Vector3(debugSphereTransform.x,
                debugSphereTransform.y + 5, debugSphereTransform.z);
            
            Pin _pin = newPinCanvas.GetComponent<Pin>();   
            _pin.pin = debugSphere;
            _pin.panel = newPinCanvas;
            _pin.position = position;
            
            // change text on prefab
            _pin.number = (pinList.Count + 1).ToString();
            _pin.pinNumber.text = "Pin #" + (pinList.Count+1).ToString();
            _pin.data = data;
            _pin.pinData.text = data;

            return _pin;
        }

        /// <summary>
        /// Removes all pins from a terrain in singleplayer. 
        /// </summary>
        public void RemoveOldPins()
        {
            if(pinList.Count > 0)
            {
                foreach (Pin pin in pinList)
                {
                    Destroy(pin.pin);
                    Destroy(pin.panel);
                }
            }
            pinList.Clear();
        }
        
        //CAPSTONE 11/14/2023 - Cole's Addition 
        /// <summary>
        /// Utilized for deleting specific user pins based on their GUID in multiplayer
        /// </summary>
        public void RemoveMyPins()
        {
            if(!NetworkManager.Singleton.IsClient) //User is NOT in multiplayer
            {
                RemoveOldPins();
            }
            else if (NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsHost) //Guest users will send the host an RPC to remove pins
            {
                pins.GetComponent<PinRPCS>().RemovePinServerRpc(SceneDownloader.singleton.guid);
            }
            else if (NetworkManager.Singleton.IsClient && NetworkManager.Singleton.IsHost)
            {
                RemovePinsWithGuid(SceneDownloader.singleton.guid);
            }
        }

        //CAPSTONE 11/14/2023 - Cole's Addition 
        /// <summary>
        /// Deletes all pins that have the passed-in guid in multiplayer.
        /// </summary>
        /// <param name="guid">Client GUID</param>
        public void RemovePinsWithGuid(string guid)
        {
           // print("Deleting pins with GUID " + guid);
            foreach (var t in pinList)
            {
                if (t != null && t.guid == guid)
                {
                    Destroy(t.pin);
                    Destroy(t.panel);
                }
            }
            pinList = pinList.Where(t => t.guid != guid).ToList();
        }
        
        /// <summary>
        /// Enables all pins in scene. Prevents issues with NetworkObjects when a user is joining a room.
        /// </summary>
        public void EnablePins()
        {
            if (pinList.Count > 0)
            {
                foreach (Pin pin in pinList)
                {
                    pin.pin.SetActive(true);
                    pin.panel.SetActive(true);
                    
                    if (GameState.InMultiuser)
                    {
                        //pins.GetComponent<PinRPCS>().PinServerRpc(pin.position, pin.data, pin.guid);
                        pin.panel.GetComponent<PerPixelSync>().pinData.Value = pin.data;
                        pin.panel.GetComponent<PerPixelSync>().pinNumber.Value = pin.number;
                        
                        pin.pin.GetComponent<NetworkObject>().Spawn();
                        pin.panel.GetComponent<NetworkObject>().Spawn(); 
                    }
                }
            }
        }

        /// <summary>
        /// Disables all pins in a scene. Prevents issues with NetworkObjects when a user is joining a room.
        /// </summary>
        public void DisablePins()
        {
            if (pinList.Count > 0)
            {
                foreach (Pin pin in pinList)
                {
                    pin.pin.SetActive(false);
                    pin.panel.SetActive(false);
                }
            }
        }

        /// <summary>
        /// Clears all Per Pixel Data
        /// </summary>
        public void ClearPerPixelData()
        {
            floatArrays.Clear();
            shortArrays.Clear();
            intArrays.Clear();
            byteArrays.Clear();

            floatDataNames.Clear();
            shortDataNames.Clear();
            intDataNames.Clear();
            byteDataNames.Clear();

            floatDataUnits.Clear();
            shortDataUnits.Clear();
            intDataUnits.Clear();
            byteDataUnits.Clear();
            
            floatOutput = "";
            shortOutput = "";
            intOutput = "";
            byteOutput = "";
        }
        #endregion
    }
} 