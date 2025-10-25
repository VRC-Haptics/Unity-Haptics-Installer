using System.Collections;
using System.Collections.Generic;
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace Editor
{
    [Serializable]
    public class NodeData
    {
        public float x;
        public float y;
        public float z;
        public string[] groups;
    }

    [Serializable]
    public class Node
    {
        public NodeData node_data;
        public string address;
        public bool is_external_address;
        public float radius;
        [JsonConverter(typeof(StringEnumConverter))]
        public HumanBodyBones target_bone;

        public Vector3 GetNodePosition()
        {
            return new Vector3(node_data.x, node_data.y, node_data.z);
        }

        public void SetPosition(Vector3 pos)
        {
            node_data.x = pos.x;
            node_data.y = pos.y;
            node_data.z = pos.z;
        }
    }

    [Serializable]
    public class Menu
    {
        public string intensity;
    }

    [Serializable]
    public class Meta
    {
        public string map_name;
        public int map_version;
        public string map_author;
        public Menu menu;
    }

    [Serializable]
    public class Config
    {
        public Node[] nodes;
        public Meta meta;
    }
}

public class ConfigClasses : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
