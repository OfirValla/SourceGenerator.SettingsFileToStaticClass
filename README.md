# SourceGenerator.SettingsFileToStaticClass
## A source generator for creating a static class ( with a constructor ) for a settings.json file

[![Build Status](https://travis-ci.org/joemccann/dillinger.svg?branch=master)](https://travis-ci.org/joemccann/dillinger)

The source generator searches for the following files:
- settings.json
- ```{AssemblyName}```.settings.json

It creates for the found json files 2 new .cs files, 
for the "settings.json" file it creates a new class called Settings.cs
and for the "```{AssemblyName}```.settings.json" it creates a new class called InternalSettings.cs ( This is for dll settings )

Note!
The namespace for the settings files is "```{AssemblyName}```.Generated", which is found in ```context.Compilation.AssemblyName```

Example: 
settings.json
```
{
  "Test": {
    "PropA": "ThisIsMySecret",
    "PropB": "00:05:00",
    "PropC": 30,
    "SubTest": {
        "PropAA": "THIS  IS MY PROP"
    }
  }
}
```

Will generate the following cs code:
```cs
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Example.Generated
{
    public static class Settings
    {
	public static Test Test{ get; private set; }

	static Settings(){
	    var assemblyLoc = System.Reflection.Assembly.GetExecutingAssembly().Location;
	    var directoryPath = System.IO.Path.GetDirectoryName(assemblyLoc);
	    var configFilePath = System.IO.Path.Combine(directoryPath, "settings.json");
	    dynamic fileContent = JsonConvert.DeserializeObject<dynamic>(System.IO.File.ReadAllText(configFilePath));
	    Test = new Test(
		PropA: fileContent.Test.PropA.ToString(),
		PropB: fileContent.Test.PropB.ToString(),
		PropC: fileContent.Test.PropC.ToString(),
		SubTest: new SubTest(
		    PropAA: fileContent.Test.SubTest.PropAA.ToString()
		)
	    );
	}
    }

    public record Test
    (
	string PropA,
	string PropB,
	int PropC,
	SubTest SubTest
    );

    public record SubTest
    (
	string PropAA
    );
}
```
