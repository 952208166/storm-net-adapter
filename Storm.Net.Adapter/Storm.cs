﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Storm
{
    public class Storm
    {
        public static Queue<Command> pendingTasks = new Queue<Command>();


        public static void LaunchPlugin(newPlugin createDelegate)
		{
            TopologyContext context = null;
            Config config = new Config();

            InitComponent(ref config, ref context);

            if (createDelegate.GetType().BaseType == typeof(ISpout))
                Context.pluginType = PluginType.SPOUT;
            else if(createDelegate.GetType().BaseType == typeof(IBolt))
                Context.pluginType = PluginType.BOLT;
            else
                Context.pluginType = PluginType.UNKNOW;

			PluginType pluginType = Context.pluginType;
			Context.Logger.Info("LaunchPlugin, pluginType: {0}", new object[]
			{
				pluginType
			});

			switch (pluginType)
			{
				case PluginType.SPOUT:
				{
					Spout spout = new Spout(createDelegate);
                    spout.Launch();
					return;
				}
				case PluginType.BOLT:
				{
					Bolt bolt = new Bolt(createDelegate);
                    bolt.Launch();
					return;
				}
				default:
				{
					Context.Logger.Error("unexpected pluginType: {0}!", new object[]
					{
						pluginType
					});
                    return;
				}
			}
		}


        /// <summary>
        /// write lines to default stream.
        /// </summary>
        /// <param name="message">stdout</param>
        public static void SendMsgToParent(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            Console.WriteLine(message);
            Console.WriteLine("end");
        }

        /// <summary>
        /// Init the Component
        /// </summary>
        public static void InitComponent(ref Config config, ref TopologyContext context)
        {
            string message = ReadMsg();

            JContainer container = JsonConvert.DeserializeObject(message) as JContainer;

            var _pidDir = container["pidDir"];
            if (_pidDir != null && _pidDir.GetType() == typeof(JValue))
            {
                string pidDir = (_pidDir as JValue).Value.ToString();
                SendPid(pidDir);
            }

            var _conf = container["conf"];
            if (_conf != null && _conf.GetType() == typeof(JContainer))
            {
                config = GetConfig(_conf as JContainer);
            }

            var _context = container["context"];
            if (_context != null && _context.GetType() == typeof(JContainer))
            {
                context = GetContext(_context as JContainer);
            }
        }

        public static Command ReadCommand()
        {
            if (pendingTasks.Count > 0)
                return pendingTasks.Dequeue();
            else
            {
                do
                {
                    string msg = ReadMsg();
                    JContainer container = JsonConvert.DeserializeObject(msg) as JContainer;

                    var _command = container["command"];
                    if (_command != null && _command.GetType() == typeof(JValue))
                    {
                        string command = (_command as JValue).Value.ToString();
                        string id = "";

                        var _id = container["id"];
                        if (_id != null && _id.GetType() == typeof(JValue))
                        {
                            id = (_id as JValue).Value.ToString();
                        }
                        return new Command(command, id);
                    }
                }
                while (true);
            }
        }

        public static StormTuple ReadTuple()
        {
            do
            {
                string msg = ReadMsg();
                JContainer container = JsonConvert.DeserializeObject(msg) as JContainer;

                int taskId = -1;
                string streamId = "", tupleId = "", component = "";
                List<object> values = new List<object>();

                try
                {
                    var _tupleId = container["id"];
                    if (_tupleId != null && _tupleId.GetType() == typeof(JValue))
                    {
                        tupleId = (_tupleId as JValue).Value.ToString();
                    }
                }
                catch { }

                try
                {
                    var _component = container["comp"];
                    if (_component != null && _component.GetType() == typeof(JValue))
                    {
                        component = (_component as JValue).Value.ToString();
                    }
                }
                catch { }

                try
                {
                    var _streamId = container["stream"];
                    if (_streamId != null && _streamId.GetType() == typeof(JValue))
                    {
                        streamId = (_streamId as JValue).Value.ToString();
                    }
                }
                catch { }

                try
                {
                    var _taskId = container["task"];
                    if (_taskId != null && _taskId.GetType() == typeof(JValue))
                    {
                        Int32.TryParse((_taskId as JValue).Value.ToString(), out taskId);
                    }
                }
                catch { }

                try
                {
                    var _values = container["tuple"];
                    if (_values != null && _values.GetType() == typeof(JArray))
                    {
                        foreach (var item in _values as JArray)
                        {
                            values.Add((item as JValue).Value);
                        }
                    }
                }
                catch { }

                if (!string.IsNullOrWhiteSpace(tupleId))
                    return new StormTuple(values, taskId, streamId, tupleId, component);
            }
            while (true);
        }

        /// <summary>
        /// reads lines and reconstructs newlines appropriately
        /// </summary>
        /// <returns>the stdin message string</returns>
        public static string ReadMsg()
        {
            StringBuilder message = new StringBuilder();

            do
            {
                string line = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                    Context.Logger.Error("Read EOF from stdin");

                if (line == "end")
                    break;

                message.AppendLine(line);
            }
            while (true);

            return message.ToString();
        }

        public static void ReportError(string message)
        {
            SendMsgToParent("{\"command\": \"error\", \"msg\": " + message + "}");
        }

        public static void Ack(StormTuple tuple)
        {
            SendMsgToParent("{\"command\": \"ack\", \"id\": " + tuple.GetTupleId() + "}");
        }

        public static void Fail(StormTuple tuple)
        {
            SendMsgToParent("{\"command\": \"fail\", \"id\": " + tuple.GetTupleId() + "}");
        }

        public static void RpcMetrics(string name, string parms)
        {
            SendMsgToParent("{\"command\": \"metrics\", \"name\": " + name + ", \"params\": " + parms + "}");
        }

        public static void Sync()
        {
            SendMsgToParent("{\"command\": \"sync\"}");
        }

        /// <summary>
        /// sent pid to storm and create a pid file
        /// </summary>
        /// <param name="heartBeatDir">the heart beat dir</param>
        private static void SendPid(string heartBeatDir)
        {
            Process currentProcess = Process.GetCurrentProcess();
            int pid = currentProcess.Id;
            SendMsgToParent("{\"pid\": " + pid.ToString() + "}");
            File.WriteAllText(heartBeatDir + "/" + pid.ToString(), "");
        }

        private static Config GetConfig(JContainer configContainer)
        {
            Config config = new Config();

            foreach (var item in configContainer)
            {
                if (item.GetType().BaseType == typeof(JProperty))
                {
                    JProperty temp = item as JProperty;

                    if (temp.Value.GetType() == typeof(JValue))
                        config.StormConf.Add(temp.Name, (temp.Value as JValue).Value);
                }
            }
            return config;
        }

        private static TopologyContext GetContext(JContainer contextContainer)
        {
            try
            {
                int taskId = -1;
                Dictionary<int, string> component = new Dictionary<int, string>();

                var _taskId = contextContainer["taskid"];
                if (_taskId.GetType().BaseType == typeof(JValue))
                    Int32.TryParse((_taskId as JValue).Value.ToString(), out taskId);

                var _component = contextContainer["task->component"];
                if (_component != null && _component.GetType() == typeof(JContainer))
                {
                    foreach (var item in _component)
                    {
                        if (item.GetType().BaseType == typeof(JProperty))
                        {
                            JProperty temp = item as JProperty;

                            if (temp.Value.GetType() == typeof(JValue))
                                component.Add(Convert.ToInt32(temp.Name), (temp.Value as JValue).Value.ToString());
                        }
                    }
                }

                return new TopologyContext(taskId, "", component);

            }
            catch (Exception ex)
            {
                Context.Logger.Error(ex.ToString());
                return null;
            }
        }
    }

    public class Spout
    {
		private newPlugin _createDelegate;
		private ISpout _spout;
		private SpoutContext _ctx;
        public Spout(newPlugin createDelegate)
		{
			this._createDelegate = createDelegate;
		}
		public void Launch()
		{
			Context.Logger.Info("[Spout] Launch ...");
			this._ctx = new SpoutContext();
			IPlugin iPlugin = this._createDelegate(this._ctx);
			if (!(iPlugin is ISpout))
			{
				Context.Logger.Error("[Spout] newPlugin must return ISpout!");
				//Context.Logger.Error("[Spout] newPlugin must return ISpout!");
			}
			this._spout = (ISpout)iPlugin;
			Stopwatch stopwatch = new Stopwatch();
			while (true)
			{
                try
                {
                    stopwatch.Restart();
                    Command command = Storm.ReadCommand();
                    if (command.Name == "next")
                    {
                        this._spout.NextTuple();
                    }
                    else if (command.Name == "ack")
                    {
                        long seqId = long.Parse(command.Id);
                        this._spout.Ack(seqId);
                    }
                    else if (command.Name == "fail")
                    {
                        long seqId = long.Parse(command.Id);
                        this._spout.Fail(seqId);
                    }
                    else
                    {
                        Context.Logger.Error("[Spout] unexpected message.");
                        //Context.Logger.Error("[Spout] unexpected message.");
                    }
                    Storm.Sync();
                    stopwatch.Stop();
                }
                catch (Exception ex)
                {
                    Context.Logger.Error(ex.ToString());
                }
			}
		}
    }

    public class Bolt
    {
		private newPlugin _createDelegate;
		private IBolt _bolt;
		private BoltContext _ctx;
		public Bolt(newPlugin createDelegate)
		{
			this._createDelegate = createDelegate;
		}
		public void Launch()
		{
			Context.Logger.Info("[Bolt] Launch ...");
			this._ctx = new BoltContext();
			IPlugin iPlugin = this._createDelegate(this._ctx);
			if (!(iPlugin is IBolt))
			{
				Context.Logger.Error("[Bolt] newPlugin must return IBolt!");
				//Context.Logger.Error("[SetBolt] newPlugin must return IBolt!");
			}
			this._bolt = (IBolt)iPlugin;
			Stopwatch stopwatch = new Stopwatch();
			while (true)
			{
				stopwatch.Restart();
                StormTuple tuple = Storm.ReadTuple();
                if (tuple.IsHeartBeatTuple())
                    Storm.Sync();
                else
                {
                    try
                    {
                        this._bolt.Execute(tuple);
                        this._ctx.Ack(tuple);
                    }
                    catch (Exception ex)
                    {
                        Context.Logger.Error(ex.ToString());
                        this._ctx.Fail(tuple);
                    }
                }
				stopwatch.Stop();
			}
		}
    }
}