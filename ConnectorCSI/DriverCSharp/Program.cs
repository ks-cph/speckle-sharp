using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using CSiAPIv1;
using SpeckleConnectorCSI;

namespace DriverCSharp
{
  class Program
  {
    private const string ProgID_SAP2000 = "CSI.SAP2000.API.SapObject";
    private const string ProgID_ETABS = "CSI.ETABS.API.ETABSObject";
    private const string ProgID_CSiBridge = "CSI.CSiBridge.API.SapObject";
    private const string ProgID_SAFE = "CSI.SAFE.API.SAFEObject";

    static int Main(string[] args)
    {
//#if DEBUG
//      Debugger.Launch();
//#endif

      // Use ret to check if functions return successfully (ret = 0) or fail (ret = nonzero)
      int ret = -1;

      // create API helper object
      cHelper myHelper = new Helper();
      var sapObject = myHelper.CreateObjectProgID(ProgID_CSiBridge);
      ret = sapObject.ApplicationStart();
      ret = sapObject.SetAsActiveObject();

      // Get a reference to cSapModel to access all API classes and functions
      cSapModel mySapModel = sapObject.SapModel;

      // call Speckle plugin
      try
      {
        cPlugin p = new cPlugin();
        cPluginCallback cb = new PluginCallback();

        // DO NOT return from SpeckleConnectorETABS.cPlugin.Main() until all work is done.
        p.Main(ref mySapModel, ref cb);
        if(cb.Finished == true)
        { Environment.Exit(0); }

        return cb.ErrorFlag;
      }
      catch (Exception ex)
      {
        MessageBox.Show("Failed to call plugin: " + ex.Message);

        ret = -3;
        return ret;
      }
    }
  }
}
