using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Sii;

namespace Sandbox
{
    internal static class Program
    {
        private static void Main( string[] args )
        {
            var text = default( string );
            using( var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream( "Sandbox.TestFile.txt" ) )
            using( var reader = new StreamReader( stream ) )
                text = reader.ReadToEnd();

            // All classes that appear in the SII document must be passed into the constructor.
            var document = new SiiDocument( typeof( AccessoryEngineData ) );
            //var document = new SiiDocument(typeof(AccessorySoundData), typeof(SoundData), typeof(SoundEngineData));

            // Load the entire document.
            document.Load( text );

            // Get specific unit definition.
            List<string> keys = new List<string>(document.Definitions.Keys);
            var engineData = document.GetDefinition<AccessoryEngineData>(keys[0]);

            // Debug stuff, just printing out all the fields
            var type = engineData.GetType();
            var className = type.GetCustomAttribute<SiiUnitAttribute>().ClassName;
            Console.WriteLine( "{2} = {0} ({1})", type.Name, className, keys[0] );

            var properties = type.GetProperties( BindingFlags.Public | BindingFlags.Instance );
            foreach( var property in properties )
            {
                var attribute = property.GetCustomAttribute<SiiAttributeAttribute>().Name;
                var getter = property.GetGetMethod();

                Console.WriteLine( "  - {0} = {1}", attribute, getter.Invoke( engineData, null ) );
            }

            Console.Read();
        }
    }
}
