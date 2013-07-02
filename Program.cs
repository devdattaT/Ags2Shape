/*
 * Copyright 2013 Devdatta Tengshe
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *    http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/



using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.IO;
using System.Web;


using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using DotSpatial.Data;
using DotSpatial.Topology;


namespace Ags2Shp
{
    /// <summary>
    /// A Struct to bind all the required information together
    /// </summary>
    struct LayerSchema
    {
        public string geomType;
        public JArray Fields;
        public JObject SR;
    }


    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Welcome to the data Downloader");
            Console.WriteLine("Please enter the output path, including the .shp");
           string op_path = Console.ReadLine();
            Console.WriteLine("Please enter the url of the Layer to be downloaded");
            string ip_url = Console.ReadLine();
            try
            {
                DownloadLayer(ip_url, op_path);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }


            Console.Read();
        }

        private static void DownloadLayer(string ip_url, string op_path)
        {

            //try to get the information about the Layer
           LayerSchema layerInfo= GetlayerInfo(ip_url);
           //now we will write the empty Shapefile
           WriteEmpty(op_path,layerInfo);

            //get the maximum count of records we will get in one query
           int maxRecordCount = GetMaxRecordCount(ip_url);

              string whereClause = "1=1";
            //get the Ids
             List<int> Ids = GetIds(ip_url, whereClause);
             int RecordCount = Ids.Count;

            /*we need to splice and request for only a subset, if number of results
             * is more that the max record count*/
             if (RecordCount > maxRecordCount)
             {
                 int startIndex = 0;
                 while (startIndex < RecordCount)
                 {
                     int length = maxRecordCount;
                     if (startIndex+maxRecordCount>RecordCount) //in the last iteration, get the count correctly
                     {
                         length = RecordCount - startIndex;
                     }

                     Console.WriteLine("Downloading Features From: "+ startIndex.ToString());
                     List<int> IdCopy = Ids.GetRange(startIndex, length);
                     GetAndSaveResults(ip_url, op_path, IdCopy, layerInfo.geomType);
                     startIndex += maxRecordCount;
                     Console.WriteLine("...");
                 }
                 
             }
             else
             {
                 Console.WriteLine("Getting Results");
                 GetAndSaveResults(ip_url, op_path, Ids, layerInfo.geomType);
                 Console.WriteLine("...");
             }

             Console.WriteLine("Finished download!");

            
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="ip_url">The URL of the Layer in the MapService</param>
        /// <param name="op_path">The complete output location</param>
        /// <param name="Ids">The List of Ids to Download and save</param>
        /// <param name="geomType">The Geometry type</param>
        private static void GetAndSaveResults(string ip_url, string op_path, List<int> Ids, string geomType)
        {
           
            List<string> Fields = new List<string>();

            string obClause = "&objectIds=";
            foreach (int i in Ids)
            {
                obClause = obClause + i.ToString() + @"%2C";
            }

            //now make the outfields
            string fieldClause = "&outFields=";

            IFeatureSet fs =FeatureSet.Open(op_path);
            System.Data.DataColumn[] columns=fs.GetColumns();


            for (int i = 0; i < columns.Length; i++)
            {
                Fields.Add(columns[i].ColumnName);
                fieldClause = fieldClause + columns[i].ColumnName + @"%2C";
            }

            string queryClause = ip_url + @"/query/?f=json&returnGeometry=true" + fieldClause + obClause;
            string result = GetURLResponse(queryClause);

            //now that we have the result, we need to parse and save it
            JObject data = JObject.Parse(result);
            JArray Features = (JArray)data["features"];
            if (Features!=null)
            {
                for (int i = 0; i < Features.Count; i++)
                {
                    JObject f = (JObject)Features[i];
                    JObject attrib = (JObject)f["attributes"];
                    JObject geom = (JObject)f["geometry"];

                    IBasicGeometry geometry = ConstructGeometry(geom, geomType);
                    IFeature Feat=fs.AddFeature(geometry);
                    Feat.DataRow.BeginEdit();
                    foreach (string field in Fields)
                    {
                        Object value=attrib[field];
                        if ((value != null)&&(attrib[field].ToString()!=""))
	                        {
                                Feat.DataRow[field] = value;
	                        }
                        
                    }
                    Feat.DataRow.EndEdit();
                    
                    
                }
                
                fs.Save();
            }
        }

        
        /// <summary>
        /// Function to get all the Ids that meet our criteria in the given Layer
        /// </summary>
        /// <param name="ip_url">The URL of the Layer in the MapService</param>
        /// <param name="whereClause">The where clause used to filter the records</param>
        /// <returns>The List of Ids</returns>
        private static List<int> GetIds(string ip_url, string whereClause)
        {
            Console.WriteLine("Getting Object Ids");
            List<int> output= new List<int>();

            string queryURL = ip_url + @"/query/?f=json&returnIdsOnly=true&where=" + System.Web.HttpUtility.UrlEncode(whereClause);
            string queryResponse = GetURLResponse(queryURL);
            JObject result = JObject.Parse(queryResponse);
            if (result.Count == 2)
            {
                JArray ids = (JArray)result["objectIds"];
                for (int i = 0; i < ids.Count; i++)
                {
                    Int32 id = Int32.Parse(ids[i].ToString());
                    output.Add(id);
                }
               
            }
            else
            {
                throw new Exception("Unexpected response from Server");
            }
            Console.WriteLine("...");
            
            return output;
        }

        /// <summary>
        /// Function to figure out how many records are sent from the server in one go
        /// </summary>
        /// <param name="ip_url">The URL of the Layer in the MapService</param>
        /// <returns>The limt of number of records that can be sent by the server in one go</returns>
        private static int GetMaxRecordCount(string ip_url)
        {
            Console.WriteLine("Getting Max feature Count");
            int output;
            string whereClause = "1=1";
            string queryURL = ip_url + @"/query/?f=json&returnIdsOnly=false&returnGeometry=false&outFields=*&where=" + System.Web.HttpUtility.UrlEncode(whereClause);
            string queryResponse = GetURLResponse(queryURL);
            JObject result = JObject.Parse(queryResponse);
            if (result.Count == 4)
            {
                JArray fs = (JArray)result["features"];
                output = fs.Count;
            }
            else
            {
                throw new Exception("Unexpected response from Server");
            }
            Console.WriteLine("...");
            return output;
        }

        /// <summary>
        /// Function to query the URL, and construct the Layer Schema from it
        /// </summary>
        /// <param name="ip_url">The URL of the Layer in the MapService</param>
        /// <returns>The constructed layer Schema</returns>
        private static LayerSchema GetlayerInfo(string ip_url)
        {
            Console.WriteLine("Trying to retrive the information from the Layer");
            String infoUrl = ip_url + @"?f=json";

            string infoResponse = GetURLResponse(infoUrl);
            LayerSchema layer = new LayerSchema();

            JObject LayerInfo = JObject.Parse(infoResponse);
            Console.WriteLine();
            if (LayerInfo.Count > 2)
            {
                String name = LayerInfo["name"].ToString();
                String type = LayerInfo["type"].ToString();
                //check that we have a feature layer
                if (!type.Equals("Feature Layer"))
                {
                    throw new Exception("Not a Feature Layer");
                }

                String geomType = LayerInfo["geometryType"].ToString();
                JObject extent = (JObject)LayerInfo["extent"];
                JObject SR = (JObject)extent["spatialReference"];
                JArray Fields = (JArray)LayerInfo["fields"];
                String capabilities = LayerInfo["capabilities"].ToString();
                //confirm that we can query on the Layer
                if (capabilities.IndexOf("Query", 0) < 0)
                {
                    throw new Exception("Cannot Query on given Layer");
                }

               
                layer.Fields = Fields;
                layer.geomType = geomType;
                layer.SR = SR;
                Console.WriteLine("...");
                Console.WriteLine("Finshed retriving information");
                Console.WriteLine("....");
               
            }
            else
            {
                //see if we have an error response is from ArcGIS Server
                if (LayerInfo.Count == 2)
                {
                    JObject error = (JObject)LayerInfo["error"];
                    String msg = error["message"].ToString();
                    Console.WriteLine("There was an error retriving information. The Server sent the following message:");
                    Console.WriteLine(msg);
                    throw new Exception("Error from Server");
                }
                else
                {
                    throw new Exception("Response is not in expected format");
                }
            }
            return layer;
        }

        /// <summary>
        /// Function to Create an Empty Shapefile
        /// </summary>
        /// <param name="op_path">The complete output location</param>
        /// <param name="layer">The layerschema information</param>
        private static void WriteEmpty(string op_path,LayerSchema layer)
        {
            Console.WriteLine("Creating Shapefile at: "+ op_path);

            string geomType = layer.geomType;
            JArray Fields = layer.Fields;
            FeatureSet fs ;
            //set the feature type
            //we will only work with the 3 standard shapes for now
            switch (geomType)
            {
                case "esriGeometryPolygon":
                    fs = new FeatureSet(FeatureType.Polygon);
                    break;
                case "esriGeometryPolyline":
                    fs = new FeatureSet(FeatureType.Line);
                    break;
                case "esriGeometryPoint":
                     fs = new FeatureSet(FeatureType.Point);
                     break;
                default:
                    //one of the others
                    throw new Exception("Unsported Geometry Type");
                    break;
            }

            //add the Colums
            for (int i = 0; i < Fields.Count; i++)
            {
                JObject field = (JObject)Fields[i];
                if (field!=null)
                {
                    //add the individual fields, but only of a few types
                    string name = field["name"].ToString();
                    string type = field["type"].ToString();
                                       
                    switch (type)
                    {
                        case "esriFieldTypeString"://type is string
                            fs.DataTable.Columns.Add(name, typeof(string));
                            break;
                        case "esriFieldTypeFloat"://type is float                            
                            fs.DataTable.Columns.Add(name, typeof(float));
                            break;
                        case "esriFieldTypeDouble"://type is double                            
                            fs.DataTable.Columns.Add(name, typeof(double));
                            break;
                        case "esriFieldTypeSmallInteger"://type is small                            
                            fs.DataTable.Columns.Add(name, typeof(Int16));
                            break;
                        case "esriFieldTypeInteger"://type is Int                            
                            fs.DataTable.Columns.Add(name, typeof(Int32));
                            break;
                        case "esriFieldTypeDate"://type is Date Time                            
                            fs.DataTable.Columns.Add(name, typeof(DateTime));
                            break;
                        default:
                            //do nothing on others
                            break;
                    }

                }
               
            }

            //try dealing with projection

            //first see if the wkt is set
            String wkt=layer.SR["wkt"].ToString();
            if (wkt.Length>10)
	            {
                    fs.ProjectionString = wkt;
		    
	            }
            else  //otherwise see if we can use the WKID
                {
                    String wkid=layer.SR["wkid"].ToString();
                    int epsgCode=0;
                    if (Int32.TryParse(wkid,out epsgCode))
                    {
                        if(epsgCode==102100) //ArcGIS server uses this for webMercator
                        {
                            epsgCode=3857;
                        }
                        if ((epsgCode>=1000)&&(epsgCode<=32768))
	                        {//only if it is within these two, can it be a valid EPSG code
		                            DotSpatial.Projections.ProjectionInfo prj = new DotSpatial.Projections.ProjectionInfo();
                                    prj.EpsgCode = epsgCode;
                                    fs.Projection = prj;
	                        }
                    }
                }

           

            //now save the empty Shapefile
            fs.SaveAs(op_path, true);
            Console.WriteLine("Finshed creating shapefile");
        }
        
        /// <summary>
        /// Function to request for the resource at the given input, and build an output string
        /// </summary>
        /// <param name="Url">The URL to request to</param>
        /// <returns>The response as a String</returns>
        private static String GetURLResponse(String Url)
        {
            String output = "";
            Stream stream=null;
            try
            {
                WebRequest req = HttpWebRequest.Create(Url);
                stream = req.GetResponse().GetResponseStream();
                Encoding encode = System.Text.Encoding.GetEncoding("utf-8");
                StreamReader sr = new StreamReader(stream, encode);


                Char[] read = new Char[256];
                int count = sr.Read(read, 0, read.Length);
                while (count > 0)
                {
                    String str = new String(read, 0, count);
                    output += str;
                    count = sr.Read(read, 0, read.Length);
                }
                
            }
            catch (Exception ex)
            {
                throw ex; //we'll handle it outside
            }
            finally
            {
                if (stream!=null)
                {
                    stream.Close();    
                }
                
            }

            return output;
        }

        #region "Geometry Operations"

        /// <summary>
        /// Function construct the proper Geometry for a given JSON Object
        /// </summary>
        /// <param name="geom">The JObject representing the geometry part of the feature from the resulting features</param>
        /// <param name="geomType">The String indicating the Geometry Type</param>
        /// <returns>The Geometry as an IBasicGeometry</returns>
        private static IBasicGeometry ConstructGeometry(JObject geom, string geomType)
        {
            IBasicGeometry geometry = null;
            switch (geomType)
            {
                case "esriGeometryPoint":
                    geometry = GetPointGeometry(geom);
                    break;
                case "esriGeometryPolyline":
                    geometry = GetLineGeometry(geom);
                    break;
                case "esriGeometryPolygon":
                    geometry = GetPolygonGeometry(geom);
                    break;
                default:
                    break;
            }
            return geometry;
        }


        /// <summary>
        /// Get The Point Geometry
        /// </summary>
        /// <param name="geometry">The JObject containing the Geometry</param>
        /// <returns>The point Geometrt as an IBasicGeometry</returns>
        private static IBasicGeometry GetPointGeometry(JObject geometry)
        {
            double x = Double.Parse(geometry["x"].ToString());
            double y = Double.Parse(geometry["y"].ToString());
            IBasicGeometry Bgeom = new Point(x, y);
            return Bgeom;
        }


        /// <summary>
        /// Get The Line Geometry
        /// </summary>
        /// <param name="geometry">The JObject containing the Geometry</param>
        /// <returns>The Line Geometrt as an IBasicGeometry</returns>
        private static IBasicGeometry GetLineGeometry(JObject geometry)
        {
            JArray Paths = (JArray)geometry["paths"];
            List<Coordinate> PtCollection = GetPointsFromPaths(Paths);
            IBasicGeometry Bgeom = new LineString(PtCollection);
            return Bgeom;
        }


        /// <summary>
        /// Get The Polygon Geometry
        /// </summary>
        /// <param name="geometry">The JObject containing the Geometry</param>
        /// <returns>The Polygon Geometrt as an IBasicGeometry</returns>
        private static IBasicGeometry GetPolygonGeometry(JObject geometry)
        {
            JArray Paths = (JArray)geometry["rings"];
            List<Coordinate> PtCollection = GetPointsFromPaths(Paths);
            IBasicGeometry Bgeom = new LineString(PtCollection);
            return Bgeom;
        }

        /// <summary>
        /// A helper function to iterate over the paths, and construct the Geometry
        /// </summary>
        /// <param name="Paths">The Array of paths</param>
        /// <returns>A list of Coordinates</returns>
        private static List<Coordinate> GetPointsFromPaths(JArray Paths)
        {
            List<Coordinate> PtCollection = new List<Coordinate>();
            for (int i = 0; i < Paths.Count; i++)
            {
                JArray path = (JArray)Paths[i];
                for (int j = 0; j < path.Count; j++)
                {
                    JArray point = (JArray)path[j];
                    double x = Double.Parse(point[0].ToString());
                    double y = Double.Parse(point[1].ToString());
                    Coordinate c = new Coordinate(x, y);
                    PtCollection.Add(c);
                }
            }
            return PtCollection;
        }
        #endregion

    }
}
