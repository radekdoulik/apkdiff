### Release new version 0.0.12

Changes:

 * fix lines with symbol names
 * new --summary-only option
 * show entries compressed size in verbose mode
 * show assemblies uncompressed size in verbose mode

### Release new version 0.0.11

Changes:

 * fix crash when comparing with apkdesc

### Release new version 0.0.10

Changes:

 * with bs option enabled, print body size differences.
   also show body sizes summary
 * show uncompressed assembly sizes in summary
 * new descrease-is-regression option to report size
   decrease(s) larger than threshold as regression

### Release new version 0.0.9

Changes:

 * new md option to compare metadata sizes
 * new bs option to compare method body sizes

### Release new version 0.0.8

Changes:

 * new f|flat option
 * workaround signature decoder problem on .NET6
 * updated to newer S.R.Metadata package, which hopefully fixes
   the decoder problems
 * report entry compression method in verbose mode