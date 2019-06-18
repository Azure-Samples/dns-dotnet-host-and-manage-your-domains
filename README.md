---
services: Dns
platforms: dotnet
author: yaohaizh
---

# Getting started on hosting and managing your domains in C# #

          Azure DNS sample for managing DNS zones.
           - Create a root DNS zone (contoso.com)
           - Create a web application
           - Add a CNAME record (www) to root DNS zone and bind it to web application host name
           - Creates a virtual machine with public IP
           - Add a A record (employees) to root DNS zone that points to virtual machine public IPV4 address
           - Creates a child DNS zone (partners.contoso.com)
           - Creates a virtual machine with public IP
           - Add a A record (partners) to child DNS zone that points to virtual machine public IPV4 address
           - Delegate from root domain to child domain by adding NS records
           - Remove A record from the root DNS zone
           - Delete the child DNS zone


## Running this Sample ##

To run this sample:

Set the environment variable `AZURE_AUTH_LOCATION` with the full path for an auth file. See [how to create an auth file](https://github.com/Azure/azure-libraries-for-net/blob/master/AUTH.md).

    git clone https://github.com/Azure-Samples/dns-dotnet-host-and-manage-your-domains.git

    cd dns-dotnet-host-and-manage-your-domains

    dotnet restore

    dotnet run

## More information ##

[Azure Management Libraries for C#](https://github.com/Azure/azure-sdk-for-net/tree/Fluent)
[Azure .Net Developer Center](https://azure.microsoft.com/en-us/develop/net/)
If you don't have a Microsoft Azure subscription you can get a FREE trial account [here](http://go.microsoft.com/fwlink/?LinkId=330212)

---

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.