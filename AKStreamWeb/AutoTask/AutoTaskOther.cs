using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LibCommon;
using LibCommon.Structs;
using LibCommon.Structs.DBModels;
using LibLogger;
using LibZLMediaKitMediaServer.Structs.WebRequest.ZLMediaKit;

namespace AKStreamWeb.AutoTask
{
    public class AutoTaskOther
    {
        private long count = 0;

        private void run()
        {
            while (true)
            {
                if (count > 90000000000)
                {
                    count = 0;
                }

                count++;
                try
                {
                    if (count % 10 == 0) //10秒一次
                    {
                        foreach (var mediaServer in Common.MediaServerList)
                        {
                            if (mediaServer != null && mediaServer.IsKeeperRunning && mediaServer.IsMediaServerRunning)
                            {
                                ResponseStruct rs = null;
                                var ret = mediaServer.WebApiHelper.GetMediaList(new ResZLMediaKitGetMediaList
                                {
                                    Schema = "rtmp",
                                }, out rs);

                                if (ret != null && ret.Data != null && rs.Code.Equals(ErrorNumber.None))
                                {
                                    List<VideoChannelMediaInfo> tmpList = new List<VideoChannelMediaInfo>();
                                    foreach (var videoChannel in Common.VideoChannelMediaInfos)
                                    {
                                        var tmpRet = ret.Data.Find(x =>
                                            x.Stream.ToLower().Equals(videoChannel.MainId.ToLower()));
                                        if (tmpRet == null)
                                        {
                                            tmpList.Add(videoChannel);
                                        }
                                    }


                                    if (tmpList.Count > 0)
                                    {
                                        lock (Common.VideoChannelMediaInfosLock)
                                        {
                                            foreach (var tmp in tmpList)
                                            {
                                                Common.VideoChannelMediaInfos.Remove(
                                                    Common.VideoChannelMediaInfos.Find(x =>
                                                        x.MainId.Equals(tmp.MainId)));
                                                Logger.Warn(
                                                    $"[{Common.LoggerHead}]->检查ZLMediaKit的StreamList时发现->{tmp.MainId}流不存在于ZLMediaKit中，因此将其从列表中移除");
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Logger.Debug(
                                            $"[{Common.LoggerHead}]->检查ZLMediaKit的StreamList时没有发现异常");  
                                    }
                                }
                            }

                            Thread.Sleep(100);
                        }
                    }

                    if (count % 3600 == 0) //3600秒一次
                    {
                        doDeleteFor24HourAgo(); //删除24小时前被软删除的过期失效的文件
                    }

                    if (count % 3600 == 0) //3600秒一次
                    {
                        foreach (var mediaServer in Common.MediaServerList)
                        {
                            if (mediaServer != null && mediaServer.KeeperWebApi != null && mediaServer.IsKeeperRunning)
                            {
                                mediaServer.KeeperWebApi.CleanUpEmptyDir(out _);
                            }
                        }
                    }
                }
                catch
                {
                    //
                }

                Thread.Sleep(10000);
            }
        }

        private void doDeleteFor24HourAgo()
        {
            try
            {
                List<RecordFile> retList = null!;
                retList = ORMHelper.Db.Select<RecordFile>()
                    .Where(x => x.Deleted == true)
                    .Where(x => x.Undo == true)
                    .Where(x => ((DateTime) x.UpdateTime!).AddHours(24) <= DateTime.Now)
                    .ToList();

                if (retList != null && retList.Count > 0)
                {
                    var deleteFileList = retList.Select(x => x.VideoPath).ToList();
                    var deleteFileIdList = retList.Select(x => x.Id).ToList();

                    var mediaServer =
                        Common.MediaServerList.FindLast(x => x.MediaServerId.Equals(retList[0].MediaServerId));
                    if (mediaServer != null && mediaServer.IsKeeperRunning)
                    {
                        var delRet = mediaServer.KeeperWebApi.DeleteFileList(out _, deleteFileList);

                        var a = ORMHelper.Db.Update<RecordFile>().Set(x => x.UpdateTime, DateTime.Now)
                            .Set(x => x.Undo, false)
                            .Where(x => deleteFileIdList.Contains(x.Id)).ExecuteAffrows();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    $"[{Common.LoggerHead}]->删除被软删除记录文件时发生异常->{ex.Message}->{ex.StackTrace}");
            }
        }

        public AutoTaskOther()
        {
            new Thread(new ThreadStart(delegate
            {
                try
                {
                    run();
                }
                catch
                {
                }
            })).Start();
        }
    }
}