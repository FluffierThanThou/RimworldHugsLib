﻿using System;
using System.Collections.Generic;
using System.Reflection;
using HugsLib.DetourByAttribute;
using HugsLib.Utils;
using Verse;

namespace HugsLib.Core {
	/**
	 * A tool to detour calls form one method to another. Will use Community Core Library detouring, if available, and its own equivalent otherwise.
	 */
	public class DetourProvider {
		private const string CCLNamespace = "CommunityCoreLibrary";
		private const string CCLDetoursClassName = "Detours";
		private const string CCLDetourMethodName = "TryDetourFromTo";

        /**
        * keep track of performed detours
        */
        private static Dictionary<MethodInfo, MethodInfo> detours = new Dictionary<MethodInfo, MethodInfo>();
        
        /**
		 * Same as TryCompatibleDetour, but writes an error to the console on failure
		 */
        public static void CompatibleDetour(MethodInfo source, MethodInfo destination, string modName) {
			var result = TryCompatibleDetour(source, destination);
			if(!result) HugsLibController.Logger.Error("{0} failed to detour method {1} to method {2}", modName, source ?? (object)"(null)", destination ?? (object)"(null)");
		}

		/**
		 * Checks if CCL is available, and uses it to detour a method. Otherwise, TryIndepentDetour is used.
		 */
		public static bool TryCompatibleDetour(MethodInfo source, MethodInfo destination) {
			if (source == null || destination == null) return false;
			var cclMethod = TryGetCCLDetourMethod();
			if (cclMethod != null) {
				try {
					return (bool) cclMethod.Invoke(null, new object[] {source, destination});
				} catch (Exception e) {
					HugsLibController.Logger.Error("Exception while performing detour using CCL: "+e, e);
					return false;
				}
			} else {
				return TryIndepentDetour(source, destination);
			}
		}
		
		/**
		 * Performs the actual detour. Code borrowed from the CCL.
		 **/
		public static unsafe bool TryIndepentDetour(MethodInfo source, MethodInfo destination) {
            // check if already detoured, if so - error out.
            if ( detours.ContainsKey( source ) )
            {
                HugsLibController.Logger.Error( "{0} was already detoured to {1}.", source.FullName(), destination.FullName() );
                return false;
            }

            // do the detour, and add it to the list 
            detours.Add( source, destination );

            if (IntPtr.Size == sizeof(Int64)) {
				// 64-bit systems use 64-bit absolute address and jumps
				// 12 byte destructive

				// Get function pointers
				long Source_Base = source.MethodHandle.GetFunctionPointer().ToInt64();
				long Destination_Base = destination.MethodHandle.GetFunctionPointer().ToInt64();

				// Native source address
				byte* Pointer_Raw_Source = (byte*)Source_Base;

				// Pointer to insert jump address into native code
				long* Pointer_Raw_Address = (long*)(Pointer_Raw_Source + 0x02);

				// Insert 64-bit absolute jump into native code (address in rax)
				// mov rax, immediate64
				// jmp [rax]
				*(Pointer_Raw_Source + 0x00) = 0x48;
				*(Pointer_Raw_Source + 0x01) = 0xB8;
				*Pointer_Raw_Address = Destination_Base; // ( Pointer_Raw_Source + 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09 )
				*(Pointer_Raw_Source + 0x0A) = 0xFF;
				*(Pointer_Raw_Source + 0x0B) = 0xE0;
			} else {
				// 32-bit systems use 32-bit relative offset and jump
				// 5 byte destructive

				// Get function pointers
				int Source_Base = source.MethodHandle.GetFunctionPointer().ToInt32();
				int Destination_Base = destination.MethodHandle.GetFunctionPointer().ToInt32();

				// Native source address
				byte* Pointer_Raw_Source = (byte*)Source_Base;

				// Pointer to insert jump address into native code
				int* Pointer_Raw_Address = (int*)(Pointer_Raw_Source + 1);

				// Jump offset (less instruction size)
				int offset = (Destination_Base - Source_Base) - 5;

				// Insert 32-bit relative jump into native code
				*Pointer_Raw_Source = 0xE9;
				*Pointer_Raw_Address = offset;
			}

			// done!
			return true;
		}

		private static MethodInfo TryGetCCLDetourMethod() {
			var typeName = CCLNamespace + '.' + CCLDetoursClassName;
			foreach (var assembly in HugsLibUtility.GetAllActiveAssemblies()) {
				var type = assembly.GetType(typeName, false, false);
				if(type == null) continue;
				var method = type.GetMethod(CCLDetourMethodName, BindingFlags.Static | BindingFlags.Public, null, new[]{typeof(MethodInfo), typeof(MethodInfo)}, null);
				if (method == null || method.ReturnType != typeof(bool)) continue;
				return method;
			}
			return null;
		}
	}
}