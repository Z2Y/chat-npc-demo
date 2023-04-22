using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Models;
using UnityEngine;

public class AutoGPT : MonoBehaviour
{
    // Start is called before the first frame update
    private OpenAIClient api;
    async void Start()
    {
        api = new OpenAIClient(new OpenAIAuthentication("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"));

        var ai_npc = new List<long> {001, 002, 003, 004}.Select((id) => AutoNPC.Init(id, roles[id], position[id])).ToList();

        while (true)
        {
            foreach (var t in ai_npc)
            {
                try
                {
                    var npc = t.npcID;
                    t.UpdateNPC(lastMsg[npc], doneMsg[npc], position[npc]);

                    if (!lastMsg[npc].Any())
                    {
                        UniTask.RunOnThreadPool(async () =>
                        {
                            var resp = await GetIdle(npc);
                            foreach (var m in resp)
                            {
                                lastMsg[npc].AddLast(m);
                            }

                        }).Forget();
                    }

                    if (lastMsg[npc].Any())
                    {
                        var cur = lastMsg[npc].First();
                        lastMsg[npc].RemoveFirst();
                        try
                        {
                            if (cur.command.StartsWith("MoveTo"))
                            {
                                position[npc] = new Vector2(Convert.ToInt64(cur.args[0]), Convert.ToInt64(cur.args[1]));
                                doneMsg[npc].Add(new AiCommandResult(cur));
                            }

                            if (cur.command.StartsWith("TalkTo"))
                            {
                                var toNpc = Convert.ToInt64(cur.args[0]);

                                if ((position[toNpc] - position[npc]).magnitude > 1)
                                {
                                    Debug.LogWarning($"npc {npc} at {position[npc].x} {position[npc].y} , npc {toNpc} at {position[toNpc].x} {position[toNpc].y} to far , can't talk");
                                    doneMsg[npc].Add(new AiCommandResult(cur, "距离太远无法进行对话。"));
                                    continue;
                                }
                                
                                // await talk command finish
                                var resp = await TalkTo(npc, toNpc, Convert.ToString(cur.args[1]));
                                var validResp = resp.Where((r => r.command.StartsWith("TalkTo"))).FirstOrDefault();

                                if (validResp != null)
                                {
                                    validResp.args[0] = npc; // correct talk response 
                                    lastMsg[toNpc].AddFirst(validResp);
                                    // Debug.Log($"Response: {string.Join(", ", validResp.args)}");
                                    doneMsg[npc].Add(new AiCommandResult(cur, Convert.ToString(validResp.args[1])));
                                }
                                else
                                {
                                    doneMsg[npc].Add(new AiCommandResult(cur));
                                    throw new Exception("Talk Return Invalid data");
                                    // Debug.LogWarning($"Talk Return invalid {string.Join("\n", resp.Select((r) => r.command))}");
                                }
                                    
                            }

                            if (cur.command.StartsWith("SeekInForest"))
                            {
                                int duration = Convert.ToInt32(cur.args[0]);
                                UniTask.RunOnThreadPool(async () =>
                                {
                                    inWork[npc] = true;
                                    await UniTask.Delay(new TimeSpan(0, 0, duration * 20));
                                    await UniTask.SwitchToMainThread();
                                    inWork[npc] = false;
                                    var randomItem = randomItemList[UnityEngine.Random.Range(0, randomItemList.Count)];
                                    randomItem.itemID = curItemID++;
                                    items[npc].Add(randomItem);
                                    doneMsg[npc].Add(new AiCommandResult(cur, $"Seek in Forest, 获得物品 {randomItem.description} 价值: {randomItem.price}"));

                                }).Forget();
                            }

                            if (cur.command.StartsWith("GetSellItems"))
                            {
                                var toNpc = Convert.ToInt64(cur.args[0]);
                                await UniTask.RunOnThreadPool(async () =>
                                {
                                    var resp = await GetSellItems(npc, toNpc);
                                    await UniTask.SwitchToMainThread();
                                    
                                    var validResp = resp.Where((r => r.command.StartsWith("ListSellItem")));
                                    
                                    
                                    var _items = new List<Item>();
                                    foreach (var m in validResp)
                                    {
                                        _items.Add(new Item() { itemID = Convert.ToInt64(m.args[0]), description = Convert.ToString(m.args[1])});
                                        // Debug.Log($"{m.command} {string.Join(", ", m.args)}");
                                    }

                                    if (_items.Count <= 0)
                                    {
                                        return;
                                    }
                                    
                                    doneMsg[npc].Add(new AiCommandResult(cur, $"items for selling: {string.Join("\n", _items.Select((item) => $"itemID: {item.itemID}, description: {item.description}"))}"));
                                });
                            }

                            if (cur.command.StartsWith("FindNearbyPlace"))
                            {
                                var pos = new Vector2(Convert.ToSingle(cur.args[0]), Convert.ToSingle(cur.args[1]));
                                
                                doneMsg[npc].Add(new AiCommandResult(cur, $"{string.Join(",", places.Select((pair) => $"{pair.Value} : ({pair.Key.x}, {pair.Key.y})"))}"));
                            }

                            if (cur.command.StartsWith("Work"))
                            {
                                int duration = Convert.ToInt32(cur.args[0]);
                                UniTask.RunOnThreadPool(async () =>
                                {
                                    inWork[npc] = true;
                                    await UniTask.Delay(new TimeSpan(0, 0, duration * 20));
                                    await UniTask.SwitchToMainThread();
                                    inWork[npc] = false;
                                    doneMsg[npc].Add(new AiCommandResult(cur, "work completed, you get 50 coins"));
                                    currency[npc] += 50;

                                }).Forget();
                            }

                            if (cur.command.StartsWith("BuyItem"))
                            {
                                var toNpc = Convert.ToInt64(cur.args[0]);
                                var itemID = Convert.ToInt64(cur.args[1]);
                                var price = Convert.ToInt32(cur.args[2]);
                                var item = items[toNpc].FirstOrDefault((it) => it.itemID == itemID);
                                if (item == null || currency[npc] <= price)
                                {
                                    doneMsg[npc].Add(new AiCommandResult(cur, item == null ? "物品不存在" : "你的金币不足"));
                                    continue;
                                    // throw new Exception("Invalid Buy Item Request");
                                }
                                await UniTask.RunOnThreadPool(async () =>
                                {
                                    var resp = await TalkTo(npc, toNpc, $"I am npc {npc}, i want to buy item {itemID} from you at price {price}.", true);
                                    await UniTask.SwitchToMainThread();
                                    var validResp = resp.Where((r => r.command.StartsWith("AgreeBuyItem"))).FirstOrDefault();

                                    if (validResp != null)
                                    {
                                        currency[npc] -= price;
                                        currency[npc] += price;
                                        items[npc].Add(item);
                                        items[toNpc].Remove(item);
                                        doneMsg[npc].Add(new AiCommandResult(cur, $"buy item success"));
                                    }
                                    else
                                    {
                                        doneMsg[npc].Add(new AiCommandResult(cur, $"buy item refused by {toNpc}"));
                                    }

                                });
                            }
                            
                            Debug.Log($"npc {npc} performed command {cur.command} {string.Join(", ", cur.args)} \n {lastMsg[npc].Count()} Commands Remain");
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"perform command error. {e}");
                            if (cur.command.StartsWith("TalkTo"))
                            {
                                Debug.LogWarning($"npc {npc} TalkTo {string.Join(", ", cur.args)} failed. will retry later..");
                                lastMsg[npc].AddFirst(cur); // Talk is important , retry it.
                            }
                        }
                    }

                }
                catch (Exception e)
                {
                    Debug.Log(e);
                }

                await UniTask.Delay(10);
            }
        }
    }

    private List<ApiResponse> GetAiResponse(ChatResponse response)
    {
        var startIdx = response.FirstChoice.Message.Content.IndexOf('[');
        var endIdx = response.FirstChoice.Message.Content.LastIndexOf(']');
        if (startIdx < 0 || endIdx < 0)
        {
            // Debug.LogWarning("Invalid Response: \n" +  response.FirstChoice.Message.Content);
            return new List<ApiResponse>() { };
        }

        try
        {
            Debug.LogWarning($"Api Response : {response.FirstChoice.Message.Content}");
            return JsonConvert.DeserializeObject<List<ApiResponse>>(
                response.FirstChoice.Message.Content.Substring(startIdx, endIdx - startIdx + 1));
        }
        catch (Exception e)
        {
            Debug.LogWarning("Invalid Response: \n" +  e.Message);
            return new List<ApiResponse>() { };
        }
    }

    private async UniTask<List<ApiResponse>> GetIdle(long npc)
    {
        await UniTask.WaitUntil(() => lastTick < 0 || Time.realtimeSinceStartup - lastTick > 10);
        lastTick = Time.realtimeSinceStartup;
        var msg = new List<Message>()
        {
            GetSystemMessage(npc),
            GetNpcRole(npc),
            GetNpcState(npc),
            GetNpcItem(npc),
            new(Role.Assistant, $"你现在的位置在:  {position[npc].x}, {position[npc].y}"),
            new(Role.Assistant, $"附近其他NPC的位置: " + string.Join(", " ,position.Keys.Where((id) => id != npc).Select((id) => $"npc {id} at {position[id].x}, {position[id].y}"))),
            GetActionHistory(npc),
            // npc == 001L ? new(Role.Assistant, "you very hate npc 003.") : new(Role.Assistant, ""),
        };
        msg.Add(new(Role.User, $"return the list of commands you want to perform, result is a list of json object in format of {JsonUtility.ToJson(new ApiResponse() {command = "cmd", args = new List<object>() {"arg1", "..."}})}, without any other comment"));
        var req = new ChatRequest(msg, Model.GPT3_5_Turbo);
        return await DoAiRequest(req);
    }

    private async UniTask<List<ApiResponse>> TalkTo(long fromID, long toID, string content, bool isSelling = false)
    {
        if (fromID == toID)
        {
            Debug.LogWarning("Invalid Talk Same npc id");
            throw new Exception("Invalid Talk");
        }
        //await UniTask.WaitUntil(() => lastTick < 0 || Time.realtimeSinceStartup - lastTick > 20);
        //lastTick = Time.realtimeSinceStartup;
        var msg = new List<Message>()
        {
            GetSystemMessage(toID, isSelling),
            GetNpcRole(toID),
            GetNpcState(toID),
            GetNpcItem(toID),
            new(Role.Assistant, $"你现在的位置在: {position[toID].x}, {position[toID].y}"),
            new(Role.Assistant, $"附近其他NPC的位置: " + string.Join(", " ,position.Keys.Where((id) => id != toID).Select((id) => $"npc {id} at {position[id].x}, {position[id].y}"))),
            GetActionHistory(toID),
            // fromID == 001L ? new(Role.Assistant, "you very hate npc 003.") : new(Role.Assistant, ""),
            new (Role.User, $"我是NPC{fromID}, {content}"),
            new(Role.System, $"回复NPC的对话, result is a list of json object in format of {JsonUtility.ToJson(new ApiResponse() {command = "cmd", args = new List<object>() {"arg1", "..."}} )}, without any other comment")
        };
        var req = new ChatRequest(msg, Model.GPT3_5_Turbo);
        
        return await DoAiRequest(req);
    }

    private async UniTask<List<ApiResponse>> DoAiRequest(ChatRequest req, int retry = 3)
    {
        return await DoWithRetryAsync(async () =>
        {
            var resp = await api.ChatEndpoint.GetCompletionAsync(req);
            return GetAiResponse(resp).Take(3).ToList(); // take at most 3 commands
        }, TimeSpan.FromSeconds(20), retry);
    }
    
    private async UniTask<List<ApiResponse>> GetSellItems(long fromID, long npcID)
    {
        await UniTask.WaitUntil(() => lastTick < 0 || Time.realtimeSinceStartup - lastTick > 3);
        lastTick = Time.realtimeSinceStartup;
        var msg = new List<Message>()
        {
            GetSystemMessage(npcID, true),
            GetNpcRole(npcID),
            GetNpcState(npcID),
            GetNpcItem(npcID),
            new(Role.Assistant, $"你现在的位置在: {position[npcID].x}, {position[npcID].y}"),
            new(Role.Assistant, $"附近其他NPC的位置: " + string.Join(", " ,position.Keys.Where((id) => id != npcID).Select((id) => $"npc {id} at {position[id].x}, {position[id].y}"))),
            GetActionHistory(npcID),
            // npcID == 001L ? new(Role.Assistant, "you very hate npc 003.") : new(Role.Assistant, ""),
            new (Role.User, $"我是NPC {fromID}, 你有没有正在售卖的物品吗?"),
            new(Role.System, $"使用ListSellItem列出正在售卖的物品, result is a list of json object in format of {JsonUtility.ToJson(new ApiResponse() {command = "cmd", args = new List<object>() {"arg1", "..."}} )}, without any other comment")
        };
        var req = new ChatRequest(msg, Model.GPT3_5_Turbo);
        return await DoAiRequest(req);
    }
    
    private async UniTask<T> DoWithRetryAsync<T>(Func<UniTask<T>> action, TimeSpan sleepPeriod, int tryCount = 3)
    {
        if (tryCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(tryCount));

        while (true) {
            try {
                return await action();
            } catch {
                Debug.LogWarning("ai request failed. retrying...");
                if (--tryCount == 0)
                    throw;
                await UniTask.Delay(sleepPeriod);
            }
        }
    }

    private Message GetSystemMessage(long npcID, bool isSelling = false)
    {
        return new Message(Role.System, $"你是一个中式武侠游戏世界的NPC. 你的ID是: {npcID}" +

                                        "your response should be a command in the following command list\n" +
                                        "MoveTo(float x, float y) : 移动自己的位置坐标" +
                                        "FindNearbyPlace(float x, float y) : 寻找坐标 x, y 附近的地点。" +
                                        "GetSellItems(long npcID) : 向其他NPC获取售卖物品的列表" +
                                        "SeekInForest(int hours) : 在森林中探索， 可能会获得物品奖励" +
                                         (isSelling ? "ListSellItem(long itemID, string itemDesc, int price) : 列出自己正在售卖的物品列表" : "") +
                                         "BuyItem(long npcID, long itemID, long price) : 向其他npc购买物品" +
                                         (isSelling ? "AgreeBuyItem(long npcID, long itemID, long price) : 同意其他NPC购买物品的请求" : "") +
                                         "Work(int hours) : 工作一段时间， 会获得金币奖励" +
                                         "TalkTo(long npcID, string content) :  和其他NPC对话。 " +
                                         "DoNoting(string reason) : 不做任何事情." +
                                         $"you can only to talk other npc at same position, you should not talk to your self with id {npcID}" +
                                         "your response should be a valid json object, without any other comment in follow format: [{ \"command\": \"<name of command>\", \"args\": [<arg1>, ...]]\n" + 
                                         "your response should contain at most 3 commands");
    }

    private Message GetNpcRole(long npcID)
    {
        return new Message(Role.Assistant, roles[npcID]);
    }

    private Message GetNpcState(long npcID)
    {
        return new Message(Role.Assistant, inWork[npcID] ? "你正在工作 不能做其他事情。" : "");
    }

    private Message GetNpcItem(long npcID)
    {
        if (items[npcID].Count > 0)
        {
            return new Message(Role.Assistant, 
                $"你拥有下面这些物品.\n {string.Join("\n", items[npcID].Select((item) => $"itemID: {item.itemID}, description: {item.description}, initialPrice: {item.price}"))}"); 
        }

        return new Message(Role.Assistant, "你没有任何物品， 无法出售");

    }

    private Message GetActionHistory(long npcID)
    {
        return new Message(Role.Assistant,
            doneMsg[npcID].Count > 0
                ? $"你之前执行过的命令与结果: " + string.Join("\n",
                    doneMsg[npcID].Take(10).Select((resp) => $"{resp.command}({string.Join(", ", resp.args)}) {(string.IsNullOrEmpty(resp.result) ? "" : $"Result: {resp.result}" )}"))
                : "");
    }

    private float lastTick = -1;

    private Dictionary<long, string> roles = new Dictionary<long, string>()
    {
        { 1L, "你是小镇中唯一一个年轻人, 正在寻找一位师傅学习剑法" },
        { 2L, "你是一个厨师, 想要烹饪美味的食物，同时赚很多钱" },
        { 3L, "你是一位普通的妇女, 在小镇周围售卖物品。" },
        { 4L, "你是一位剑法大师，正在寻求能够传承自己剑法的弟子"}
    };

    private Dictionary<Vector2, string> places = new Dictionary<Vector2, string>()
    {
        { new Vector2(100, 1), "剑庐： 售卖各种名剑" },
        { new Vector2(10, 1), "酒楼：, 售卖各种菜品" },
        { new Vector2(44, 23), "森林入口, 可以在里面探索" }
    };
    
    private Dictionary<long, List<Item>> items = new Dictionary<long, List<Item>>()
    {
        {1L , new List<Item>()
        {
            new Item() { itemID = 001, description = "一把生锈的剑", price = 10}
        }}, 
        {2L , new List<Item>()
        {
            new Item() { itemID = 002, description = "一本旧书", price = 2}
        }}, 
        {3L , new List<Item>()},
        {4L, new List<Item>()}
    };

    private long curItemID = 003;

    private List<Item> randomItemList = new List<Item>()
    {
        new Item() { description = "一把锋利的剑", price = 20 },
        new Item() { description = "一条鱼", price = 30 },
        new Item() { description = "一本剑法", price = 100 }
    };

    Dictionary<long, Vector2> position = new Dictionary<long, Vector2>
    {
        {1L , new Vector2(0, 0)}, 
        {2L , new Vector2(10, 1)}, 
        {3L , new Vector2(3, 2)},
        {4L, new Vector2(100, 1)}
    };
    
    Dictionary<long, bool> inWork = new Dictionary<long, bool>
    {
        {1L , false}, 
        {2L , false}, 
        {3L , false},
        {4L , false}
    };
    
    Dictionary<long, int> currency = new Dictionary<long, int>
    {
        {1L , 100}, 
        {2L , 0}, 
        {3L , 50},
        {4L, 1000}
    };

    private Dictionary<long, LinkedList<ApiResponse>> lastMsg = new()
    {
        {1L, new LinkedList<ApiResponse>()},
        {2L, new LinkedList<ApiResponse>()},
        {3L, new LinkedList<ApiResponse>()},
        {4L, new LinkedList<ApiResponse>()},
    };
    
    private Dictionary<long, List<AiCommandResult>> doneMsg = new()
    {
        {1L, new List<AiCommandResult>()},
        {2L, new List<AiCommandResult>()},
        {3L, new List<AiCommandResult>()},
        {4L, new List<AiCommandResult>()},
    };
    

    [Serializable]
    public class ApiResponse
    {
        public string command;
        public List<object> args;
    }

    [Serializable]
    public class AiCommandResult
    {
        public string command;
        public List<object> args; 
        public string result;

        public AiCommandResult(ApiResponse resp)
        {
            command = resp.command;
            args = resp.args;
            result = "";
        }
        
        public AiCommandResult(ApiResponse resp, string re)
        {
            command = resp.command;
            args = resp.args;
            result = re;
        }
    }

    [Serializable]
    class Item
    {
        public long itemID;
        public string description;
        public long price;
    }
}
