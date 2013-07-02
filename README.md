This is a simple C# Scraping Script for Downloading Data from ArcGIS Server's REST Endpoint and saving it as a shapefile.

It has two dependencies:

 1. [JSON.NET](http://james.newtonking.com/projects/json-net.aspx)
 2. [DotSpatial](http://dotspatial.codeplex.com/)

*DotSpatial* requires .NET Framework 4.0 and works only on Windows. This is why It will not work with Mono.

This is Available under the Apache 2.0 License.

Known Issues: 
 1. The script only works with Points, Single Part Polygons, and Single part Lines.The Script has trouble with MultiPart Polygons, and MultiPart Lines.
 2. There are no tests in the code, since this was a one off developement, done in a single afternoon.
