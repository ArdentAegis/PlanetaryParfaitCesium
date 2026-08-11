using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics;
using TerrainEngine;

namespace TerrainEngine.Tools
{
    public class NomenclatureDataReader : MonoBehaviour
    {
        #region FIELDS
        public static NomenclatureDataReader singleton;
        
        public List<Nomenclature> nomenclaturePins;
        public GameObject nomenclatureParent;
        public GameObject pinPrefab;
        public GameObject nomenclaturePrefab;
        public bool hasNomenclature = false;

        public CesiumGeoreference georeference;
        public GameObject terrain;
        public Material material;
        public GameObject platform;
        
        #endregion

        #region MONO

        private void Awake()
        {
            singleton = this;
            nomenclaturePins = new List<Nomenclature>();
        }

        private void Update()
        {
            if (hasNomenclature)
            {
                foreach (var nom in nomenclaturePins)
                {
                    nom.pin.transform.position = terrain.transform.TransformPoint(nom.offset);
                    nom.panel.transform.localScale = new Vector3(-1, 1, 1);
                    nom.panel.transform.LookAt(platform.transform);
                }
            }
        }
        #endregion
        
        #region METHODS

        /// <summary>
        /// Instantiates nomenclature data on terrain selection.
        /// </summary>
        /// <param name="data">Nomenclature Layer Data</param>
        public void InstantiateNomenclature(JMARSScene.Layer.LayerData data)
        {
            material = SceneMaterializer.singleton.activeMaterial;
            terrain = SceneMaterializer.singleton.activeTiles;

            Texture2D heightTexture = material.GetTexture("_HeightMap") as Texture2D;
            
            foreach (var values in data.text_data)
            {
                Vector3 newPosition;

                if (SceneMaterializer.singleton.useCesium)
                {
                    // gets nomenclature position (but not height) on Cesium moon
                    double2 lonlat = terrain.GetComponent<SphereShellMaker>().PixelCoordinatesToLonLat(values.x, values.y, heightTexture);
                    newPosition = terrain.GetComponent<SphereShellMaker>().GetSpherePosition(lonlat.x, lonlat.y) + georeference.transform.position;
                    
                    // raycast down to place pin on Cesium moon
                    RaycastHit hit;
                    LayerMask layerMask = LayerMask.GetMask("Default");
                    
                    if (Physics.Raycast(newPosition + Vector3.up * 10.0f,
                                        -Vector3.up,
                                        out hit, 50f, layerMask))
                    {
                        newPosition = hit.point;
                        newPosition.y += 0.05f;
                    }
                }

                else
                {
                    // get (x, z) position in world space from texture space
                    float x_position = ((float)values.x / heightTexture.width)-0.5f;
                    float z_position = ((float)(values.y - heightTexture.height)/heightTexture.height)+0.5f;
                    
                    //get height value at (x, y) from depth texture
                    float heightValue = heightTexture.GetPixel((int)values.x, (int)(heightTexture.height - values.y)).r;
                    float h_t = heightValue * material.GetFloat("_scaleFactor"); 
                    
                    // (x, y, z) position of nomenclature in world space
                    Vector3 position = new Vector3(x_position, h_t, z_position);
                    newPosition = terrain.transform.TransformPoint(position);
                }

                Vector3 offset = terrain.transform.InverseTransformPoint(newPosition);
                
                // instantiate pin obj
                GameObject pin = Instantiate(pinPrefab, nomenclatureParent.transform, true);
                pin.transform.localPosition = newPosition;
                pin.transform.localScale = Vector3.one * 0.5f;
                pin.gameObject.name = values.name;
                
                // instantiate nomenclature object
                GameObject panel = Instantiate(nomenclaturePrefab, pin.transform, true);
                panel.transform.position = new Vector3(newPosition.x,
                    newPosition.y + 4, newPosition.z);
                
                Nomenclature n = panel.GetComponent<Nomenclature>();
                n.pin = pin;
                n.panel = panel;
                n.SetText(values.name);
                n.SetMaterials();
                n.position = newPosition;
                n.offset = offset;
                nomenclaturePins.Add(n);
            }

            hasNomenclature = true;
        }

        /// <summary>
        /// Deletes nomenclature gameObjects and clears nomenclature data list. 
        /// </summary>
        public void DeleteNomenclature()
        {
            hasNomenclature = false;
            foreach (Nomenclature nom in nomenclaturePins)
            {
                Destroy(nom.pin);
            }

            nomenclaturePins.Clear();
        }

        /// <summary>
        /// Enables all pins in scene. Prevents issues with NetworkObjects when a user is joining a room.
        /// </summary>
        public void EnablePins()
        {
            if (nomenclaturePins.Count > 0)
            {
                foreach (Nomenclature n in nomenclaturePins)
                {
                    n.pin.SetActive(true);
                }
            }
        }

        /// <summary>
        /// Disables all pins in a scene. Prevents issues with NetworkObjects when a user is joining a room.
        /// </summary>
        public void DisablePins()
        {
            if (nomenclaturePins.Count > 0)
            {
                foreach (Nomenclature n in nomenclaturePins)
                {
                    n.pin.SetActive(false);
                }
            }
        }
        
        #endregion
    }
}
