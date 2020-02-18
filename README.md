**apkdiff** is a tool to compare Android packages

```
Usage: apkdiff.exe OPTIONS* <package1.apk> <package2.apk>

Compares APK packages content or APK package with content description

Copyright 2020 Microsoft Corporation

Options:
  -c, --comment=VALUE        Comment to be saved inside .apkdesc file
  -h, --help, -?             Show this message and exit
  -s, --save-descriptions    Save .apkdesc files next to the apk package(s)
  -v, --verbose              Output information about progress during the run
                               of the tool
```

It can be use to compare Android packages (apk's) and/or apk
descriptions files (apkdesc)

### Example usage

```
mono apkdiff.exe xa-d16-4/bin/TestRelease/Xamarin.Forms_Performance_Integration.apkdesc xa-d16-5/bin/TestRelease/Xamarin.Forms_Performance_Integration.apk
Size difference in bytes ([*1] apk1 only, [*2] apk2 only):
  +       49184 lib/armeabi-v7a/libmonosgen-2.0.so
  +       13824 assemblies/Mono.Android.dll
  +       10824 lib/x86/libmonodroid.so
  +        5604 lib/armeabi-v7a/libmonodroid.so
  +        1864 lib/armeabi-v7a/libxamarin-app.so
  +        1864 lib/x86/libxamarin-app.so
  +         168 classes.dex
  -        3584 assemblies/System.dll
  -       10240 assemblies/mscorlib.dll
  -       71680 assemblies/Mono.Security.dll
  -       77792 lib/x86/libmonosgen-2.0.so
Summary:
  -       46984 Package size difference
```

### Example output with shared libraries and assemblies details
```
mono apkdiff.exe Xamarin.Forms_Performance_Integration-Signed-NewNDK-Default.apk Xamarin.Forms_Performance_Integration-Signed-NewNTR-Default.apk 
Size difference in bytes ([*1] apk1 only, [*2] apk2 only):
  +       42496 assemblies/Mono.Android.dll
    +             Type Java.Nio.ByteBuffer
    +             Type Java.Nio.ByteBuffer/__<$>_jni_marshal_methods
    +             Type Java.Nio.ByteBufferInvoker
    +             Type Java.Nio.Channels.FileChannel
    +             Type Java.Nio.Channels.FileChannel/__<$>_jni_marshal_methods
    +             Type Java.Nio.Channels.FileChannelInvoker
    +             Type Java.Nio.Channels.FileChannelInvoker/__<$>_jni_marshal_methods
    +             Type Java.Nio.Channels.IByteChannel
    +             Type Java.Nio.Channels.IByteChannelInvoker
    +             Type Java.Nio.Channels.IByteChannelInvoker/__<$>_jni_marshal_methods
    +             Type Java.Nio.Channels.IChannel
    +             Type Java.Nio.Channels.IChannelInvoker
    +             Type Java.Nio.Channels.IChannelInvoker/__<$>_jni_marshal_methods
    +             Type Java.Nio.Channels.IGatheringByteChannel
    +             Type Java.Nio.Channels.IGatheringByteChannelInvoker
    +             Type Java.Nio.Channels.IGatheringByteChannelInvoker/__<$>_jni_marshal_methods
    +             Type Java.Nio.Channels.IInterruptibleChannel
    +             Type Java.Nio.Channels.IInterruptibleChannelInvoker
    +             Type Java.Nio.Channels.IInterruptibleChannelInvoker/__<$>_jni_marshal_methods
    +             Type Java.Nio.Channels.IReadableByteChannel
    +             Type Java.Nio.Channels.IReadableByteChannelInvoker
    +             Type Java.Nio.Channels.IReadableByteChannelInvoker/__<$>_jni_marshal_methods
    +             Type Java.Nio.Channels.IScatteringByteChannel
    +             Type Java.Nio.Channels.IScatteringByteChannelInvoker
    +             Type Java.Nio.Channels.IScatteringByteChannelInvoker/__<$>_jni_marshal_methods
    +             Type Java.Nio.Channels.ISeekableByteChannel
    +             Type Java.Nio.Channels.ISeekableByteChannelInvoker
    +             Type Java.Nio.Channels.ISeekableByteChannelInvoker/__<$>_jni_marshal_methods
    +             Type Java.Nio.Channels.IWritableByteChannel
    +             Type Java.Nio.Channels.IWritableByteChannelInvoker
    +             Type Java.Nio.Channels.IWritableByteChannelInvoker/__<$>_jni_marshal_methods
    +             Type Java.Nio.Channels.Spi.AbstractInterruptibleChannel
    +             Type Java.Nio.Channels.Spi.AbstractInterruptibleChannel/__<$>_jni_marshal_methods
    +             Type Java.Nio.Channels.Spi.AbstractInterruptibleChannelInvoker
    +             Type Java.IO.FileInputStream
    +             Type Java.IO.FileInputStream/__<$>_jni_marshal_methods
  +        6264 lib/armeabi-v7a/libxamarin-app.so
                  Symbol size difference
    +        3480 mj_typemap
    +        2784 jm_typemap
  +        6264 lib/x86/libxamarin-app.so
                  Symbol size difference
    +        3480 mj_typemap
    +        2784 jm_typemap
  +        3584 assemblies/Java.Interop.dll
    -             Type Java.Interop.JniRuntime/JniValueManager/<>c__DisplayClass38_0
    -             Type Java.Interop.JniRuntime/JniTypeManager/<CreateGetTypesEnumerator>d__18
    -             Type Java.Interop.JniRuntime/JniTypeManager/<CreateGetTypesForSimpleReferenceEnumerator>d__20
    +             Type Microsoft.CodeAnalysis.EmbeddedAttribute
    +             Type System.Runtime.CompilerServices.NullableAttribute
    +             Type System.Diagnostics.CodeAnalysis.AllowNullAttribute
    +             Type System.Diagnostics.CodeAnalysis.MaybeNullAttribute
    +             Type System.Diagnostics.CodeAnalysis.NotNullAttribute
    +             Type System.Diagnostics.CodeAnalysis.NotNullWhenAttribute
    +             Type System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute
    +             Type Java.Interop.JniRuntime/JniValueManager/<>c__DisplayClass37_0
    +             Type Java.Interop.JniRuntime/JniTypeManager/<CreateGetTypesEnumerator>d__17
    +             Type Java.Interop.JniRuntime/JniTypeManager/<CreateGetTypesForSimpleReferenceEnumerator>d__19
  -         512 assemblies/mscorlib.dll
Summary:
  +       45056 Package size difference
```
