﻿/*
 *
 * (c) Copyright Ascensio System SIA 2022
 *
 * The MIT License (MIT)
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
*/

using Microsoft.SharePoint;
using System;
using System.Web;
using System.Web.Script.Serialization;
using System.Security.Cryptography;
using System.Net;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace Onlyoffice
{
    public class CallbackHandler : IHttpHandler
    {
        protected int secret; 

        public void ProcessRequest(HttpContext context)
        {
            Dictionary<string, object> validData = new Dictionary<string, object>();
            string data = context.Request["data"];

            bool isValidData = false;
            string  action = "",
                    url = HttpUtility.HtmlEncode(HttpContext.Current.Request.Url.Scheme) + "://" + HttpContext.Current.Request.Url.Authority +
                                                                                                            HttpContext.Current.Request.RawUrl.Substring(0, HttpContext.Current.Request.RawUrl.IndexOf("_layouts"));
            //get secret key
            SPSecurity.RunWithElevatedPrivileges(delegate()
            {
                using (SPSite site = new SPSite(url))
                using (SPWeb web = site.OpenWeb())
                {
                    secret = Convert.ToInt32(web.Properties["SharePointSecret"]);
                }
            });

            isValidData = Encryption.Decode(data, secret, validData);//check, are request data valid

            if (!isValidData)
            {
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                context.Response.StatusDescription = "Access denied.";
                return;
            }

            action = (string)validData["action"];
            if (action == "download")
            {
                Download(url, validData, context);
            }
            else if (action == "track")
            {
                Track(url, validData, context);
            }
        }

        static void Download(string url, Dictionary<string, object> data, HttpContext context)
        {
            SPUserToken userToken = null;
            string SPListURLDir = (string)data["SPListURLDir"];
            string SPListItemId = (string)data["SPListItemId"];
            int userId = (int)data["userId"];

            SPSecurity.RunWithElevatedPrivileges(delegate ()
            {
                using (SPSite site = new SPSite(url))
                using (SPWeb web = site.OpenWeb())
                {
                    try
                    {
                        SPUser user = web.AllUsers.GetByID(userId);
                        userToken = user.UserToken;

                        SPSite s = new SPSite(url, userToken);
                        SPWeb w = s.OpenWeb();

                        var list = w.GetList(SPListURLDir);
                        SPListItem item = list.GetItemById(Int32.Parse(SPListItemId));

                        //get and send file
                        SPFile file = item.File;
                        var ContentType = MimeMapping.GetMimeMapping(file.Name);
                        if (file != null)
                        {
                            byte[] bArray = file.OpenBinary();

                            context.Response.Clear();
                            context.Response.Charset = Encoding.Unicode.EncodingName;
                            context.Response.AddHeader("Content-Disposition", string.Format("attachment; filename={0}", file.Name));
                            context.Response.AddHeader("Content-Length", file.Length.ToString());
                            context.Response.BinaryWrite(bArray);
                            context.Response.ContentType = ContentType;
                            context.Response.Flush();
                            context.Response.Close();
                            context.Response.End();
                        }
                    }
                    catch (Exception ex) 
                    {
                        Log.LogError(ex.Message);
                    }
                    
                }
            });
        }

        static void Track(string url, Dictionary<string, object> data, HttpContext context)
        {
            SPUserToken userToken = null;
            string SPListURLDir = (string)data["SPListURLDir"];
            string SPListItemId = (string)data["SPListItemId"];
            string Folder = (string)data["Folder"];

            SPSecurity.RunWithElevatedPrivileges(delegate ()
            {
                using (SPSite site = new SPSite(url))
                {
                    using (SPWeb web = site.OpenWeb())
                    {
                        string body;
                        using (var reader = new StreamReader(context.Request.InputStream))
                            body = reader.ReadToEnd();
                        
                        var fileData = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(body);
                        try
                        { 
                            var userList = (System.Collections.ArrayList)fileData["users"];
                            var userID = Int32.Parse(userList[0].ToString());

                            SPUser user = web.AllUsers.GetByID(userID);
                            userToken = user.UserToken;
                        }
                        catch (Exception ex) {

                            Log.LogError(ex.Message);
                            userToken = web.AllUsers[0].UserToken; 
                        }

                        SPSite s = new SPSite(url, userToken);
                        SPWeb w = s.OpenWeb();
                        var list = w.GetList(SPListURLDir);

                        try
                        {
                            SPListItem item = list.GetItemById(Int32.Parse(SPListItemId));
                            
                            //save file to SharePoint
                            if ((int)fileData["status"] == 2)
                            {
                                var req = (string)fileData["url"];

                                var replaceExistingFiles = true;

                                var fileName = item.File.Name;
                            
                                w.AllowUnsafeUpdates = true; //for list update in SharePoint necessary AllowUnsafeUpdates = true
                                w.Update();

                                byte[] fileDataArr = null;
                                using (var wc = new WebClient())
                                    fileDataArr = wc.DownloadData(req);

                                if (Folder != "")
                                {
                                    SPFolder folder = w.GetFolder(Folder);
                                    folder.Files.Add(fileName, fileDataArr, replaceExistingFiles);
                                    folder.Update();
                                }
                                else
                                {
                                    list.RootFolder.Files.Add(fileName, fileDataArr, replaceExistingFiles);
                                    list.Update();
                                }

                                w.AllowUnsafeUpdates = false;
                                w.Update();
                            }
                            context.Response.Write("{\"error\":0}");
                        }
                        catch (Exception ex)
                        {
                            Log.LogError(ex.Message);
                        }
                    }
                }
            });
        }

        public bool IsReusable
        {
            get { return false; }
        }
    }
}
