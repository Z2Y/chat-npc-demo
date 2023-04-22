using System.Collections.Generic;
using UnityEngine;


public class AutoNPC : MonoBehaviour
{
    public long npcID;

    public string role;

    public List<AutoGPT.ApiResponse> commands;

    public List<AutoGPT.AiCommandResult> history;

    public static AutoNPC Init(long npcID, string role, Vector2 position)
    {
        var obj = new GameObject(npcID.ToString(), typeof(AutoNPC));
        var npc = obj.GetComponent<AutoNPC>();
        npc.npcID = npcID;
        npc.role = role;
        npc.transform.parent = GameObject.Find("Root").transform;
        npc.transform.position = position;
        return npc;
    }

    public void UpdateNPC(IEnumerable<AutoGPT.ApiResponse> commands, IEnumerable<AutoGPT.AiCommandResult> history, Vector2 position)
    {
        this.commands = new List<AutoGPT.ApiResponse>(commands);
        this.history = new List<AutoGPT.AiCommandResult>(history);
        transform.position = position;
    }
}