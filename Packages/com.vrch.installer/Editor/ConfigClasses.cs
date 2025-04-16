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
        public float radius;
        [JsonConverter(typeof(StringEnumConverter))]
        public HumanBodyBones target_bone;
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
