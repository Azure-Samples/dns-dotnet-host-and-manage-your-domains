// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager.Resources.Models;
using Azure.ResourceManager.Samples.Common;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager;
using Azure.ResourceManager.Dns;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;
using Azure.ResourceManager.Dns.Models;
using System.Net;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Compute;
using System.Net.NetworkInformation;

namespace ManageDns
{
    public class Program
    {
        private static ResourceIdentifier? _resourceGroupId = null;

        /**
         * Azure DNS sample for managing DNS zones.
         *  - Create a root DNS zone (contoso.com)
         *  - Create a web application
         *  - Add a CNAME record (www) to root DNS zone and bind it to web application host name
         *  - Creates a virtual machine with public IP
         *  - Add a A record (employees) to root DNS zone that points to virtual machine public IPV4 address
         *  - Creates a child DNS zone (partners.contoso.com)
         *  - Creates a virtual machine with public IP
         *  - Add a A record (partners) to child DNS zone that points to virtual machine public IPV4 address
         *  - Delegate from root domain to child domain by adding NS records
         *  - Remove A record from the root DNS zone
         *  - Delete the child DNS zone
         */
        public static async Task RunSample(ArmClient client)
        {
            string rgName = Utilities.CreateRandomName("DnsTemplateRG");
            string dnsZoneName = $"{Utilities.CreateRandomName("contoso")}.com";
            string appServicePlanName = Utilities.CreateRandomName("servicePlan");
            string appName = Utilities.CreateRandomName("SampleWebApp");
            string vnetName1 = Utilities.CreateRandomName("vnet1-");
            string vnetName2 = Utilities.CreateRandomName("vnet2-");
            string publicIpName1 = Utilities.CreateRandomName("pip1-");
            string publicIpName2 = Utilities.CreateRandomName("pip2-");
            string nicName1 = Utilities.CreateRandomName("nic1-");
            string nicName2 = Utilities.CreateRandomName("nic2-");
            string vmName1 = Utilities.CreateRandomName("vm1-");
            string vmName2 = Utilities.CreateRandomName("vm2-");

            string domainName = dnsZoneName;
            string cnameRecordName = "www";
            string txtRecordName1 = "asuid.www";
            string txtRecordName2 = "asuid";
            var partnerSubDomainName = "partners." + dnsZoneName;

            try
            {
                // Get default subscription
                SubscriptionResource subscription = await client.GetDefaultSubscriptionAsync();

                // Create a resource group in the EastUS region
                Utilities.Log($"creating resource group...");
                ArmOperation<ResourceGroupResource> rgLro = await subscription.GetResourceGroups().CreateOrUpdateAsync(WaitUntil.Completed, rgName, new ResourceGroupData(AzureLocation.EastUS));
                ResourceGroupResource resourceGroup = rgLro.Value;
                _resourceGroupId = resourceGroup.Id;
                Utilities.Log("Created a resource group with name: " + resourceGroup.Data.Name);

                //============================================================, 
                // Creates root DNS Zone

                Utilities.Log("Creating root DNS zone...");
                DnsZoneData dataZoneInput = new DnsZoneData("Global") { };
                var dnsZoneLro = await resourceGroup.GetDnsZones().CreateOrUpdateAsync(WaitUntil.Completed, dnsZoneName, dataZoneInput);
                DnsZoneResource dnsZone = dnsZoneLro.Value;
                Utilities.Log("Created root DNS zone: " + dnsZone.Data.Name);

                //============================================================
                // Sets NS records in the parent zone (hosting custom domain) to make Azure DNS the authoritative
                // source for name resolution for the zone

                Utilities.Log("Go to your registrar portal and configure your domain " + dnsZoneName
                        + " with following name server addresses");
                foreach (var nameServer in dnsZone.Data.NameServers)
                {
                    Utilities.Log(" " + nameServer);
                }

                //============================================================
                // Creates a web App

                Utilities.Log("Creating Web App...");
                AppServicePlanData appServicePlanInput = new AppServicePlanData(new AzureLocation("East US"))
                {
                    Sku = new AppServiceSkuDescription()
                    {
                        Name = "P1",
                        Tier = "Premium",
                        Size = "P1",
                        Family = "P",
                        Capacity = 1,
                    },
                    Kind = "app",
                };
                ArmOperation<AppServicePlanResource> appServicePlanLro = await resourceGroup.GetAppServicePlans().CreateOrUpdateAsync(WaitUntil.Completed, appServicePlanName, appServicePlanInput);

                WebSiteData appInput = new WebSiteData(resourceGroup.Data.Location)
                {
                    AppServicePlanId = appServicePlanLro.Value.Data.Id,
                };
                ArmOperation<WebSiteResource> lro = await resourceGroup.GetWebSites().CreateOrUpdateAsync(WaitUntil.Completed, appName, appInput);
                WebSiteResource app = lro.Value;
                Utilities.Log("Created web app: " + app.Data.Name);

                //============================================================
                // Creates a CName record and bind it with the web app

                // Step 1: Adds CName DNS record to root DNS zone that specify web app host domain as an
                // alias for www.[customDomainName]

                // Create one CName record
                Utilities.Log("Updating DNS zone by adding a CName record...");

                // Create a CName record
                var cnameInput = new DnsCnameRecordData()
                {
                    TtlInSeconds = 3600,
                    Cname = app.Data.DefaultHostName
                };
                var cnameRecord = await dnsZone.GetDnsCnameRecords().CreateOrUpdateAsync(WaitUntil.Completed, cnameRecordName, cnameInput);
                Utilities.Log($"Created CName record: {cnameRecord.Value.Data.Name}");

                // Create two TXT record
                var txtRecordInput = new DnsTxtRecordData()
                {
                    TtlInSeconds = 3600,
                    DnsTxtRecords =
                    {
                        new DnsTxtRecordInfo()
                        {
                            Values = { app.Data.CustomDomainVerificationId }
                        }
                    }
                };
                var txtRecord1 = await dnsZone.GetDnsTxtRecords().CreateOrUpdateAsync(WaitUntil.Completed, txtRecordName1, txtRecordInput);
                Utilities.Log($"Created TXT record: {txtRecord1.Value.Data.Name}");

                var txtRecord2 = await dnsZone.GetDnsTxtRecords().CreateOrUpdateAsync(WaitUntil.Completed, txtRecordName2, txtRecordInput);
                Utilities.Log($"Created TXT record: {txtRecord2.Value.Data.Name}");

                // Step 2: Adds a web app host name binding for www.[customDomainName]
                //         This binding action will fail if the CName record propagation is not yet completed

                // Create a app service domain
                Utilities.Log($"Creating a app service domain...");
                RegistrationContactInfo registrationInput = new RegistrationContactInfo("test@gmail.com", "test", "test", "+86.18800001111")
                {
                    JobTitle = "Billing",
                    Organization = "Microsoft Inc.",
                    AddressMailing = new RegistrationAddressInfo("shanghai", "shanghai", "CN", "180000", "shanghai")
                };
                AppServiceDomainData appServiceDomainInput = new AppServiceDomainData("Global")
                {
                    ContactAdmin = registrationInput,
                    ContactTech = registrationInput,
                    ContactBilling = registrationInput,
                    ContactRegistrant = registrationInput,
                    IsDomainPrivacyEnabled = true,
                    Consent = new DomainPurchaseConsent()
                    {
                        AgreementKeys = { "key1" },
                        AgreedBy = "192.0.2.1"
                    },
                };
                ;
                var appServiceDomainLro = await resourceGroup.GetAppServiceDomains().CreateOrUpdateAsync(WaitUntil.Completed, domainName, appServiceDomainInput);
                AppServiceDomainResource appServiceDomain = appServiceDomainLro.Value;
                Utilities.Log($"Created app service domain: {appServiceDomain.Data.Name}");
                Utilities.Log($"Update app service domain to binding azure dns zone...");
                AppServiceDomainPatch updateDomainInput = new AppServiceDomainPatch()
                {
                    DnsType = AppServiceDnsType.AzureDns,
                    DnsZoneId = dnsZone.Id
                };
                await appServiceDomain.UpdateAsync(updateDomainInput);
                Utilities.Log($"App service domain has bound DNS zone");

                // Waiting for a minute for DNS CName entry to propagate
                Utilities.Log("Waiting two minute for records entry to propagate...");
                Thread.Sleep(120 * 1000);

                Utilities.Log("Updating Web app with host name binding...");
                HostNameBindingData bindInput = new HostNameBindingData()
                {
                    CustomHostNameDnsRecordType = CustomHostNameDnsRecordType.A,
                    HostNameType = AppServiceHostNameType.Verified,
                    SiteName = appName,
                };
                await app.AnalyzeCustomHostnameAsync(domainName);
                await app.GetSiteHostNameBindings().CreateOrUpdateAsync(WaitUntil.Completed, domainName, bindInput);
                Utilities.Log("Web app updated");

                //============================================================
                // Creates a virtual machine with public IP

                // Create vnet
                VirtualNetworkResource vnet1 = await Utilities.CreateVirtualNetwork(resourceGroup, vnetName1);
                ResourceIdentifier subnetId1 = vnet1.Data.Subnets[0].Id;
                // Create public ip
                PublicIPAddressResource publicIP1 = await Utilities.CreatePublicIP(resourceGroup, publicIpName1);
                // Create network interface
                NetworkInterfaceResource nic1 = await Utilities.CreateNetworkInterface(resourceGroup, subnetId1, publicIP1.Id, nicName1);

                Utilities.Log("Creating a virtual machine with public IP...");

                VirtualMachineData vmInput1 = Utilities.GetDefaultVMInputData(resourceGroup, vmName1);
                vmInput1.NetworkProfile.NetworkInterfaces.Add(new VirtualMachineNetworkInterfaceReference() { Id = nic1.Id, Primary = true });
                var vmLro1 = await resourceGroup.GetVirtualMachines().CreateOrUpdateAsync(WaitUntil.Completed, vmName1, vmInput1);
                VirtualMachineResource vm1 = vmLro1.Value;
                Utilities.Log($"Created virtual machine: {vm1.Data.Name}");

                //============================================================
                // Update DNS zone by adding a A record in root DNS zone pointing to virtual machine IPv4 address

                Utilities.Log("Updating root DNS zone " + dnsZoneName + "...");
                var aInput = new DnsARecordData()
                {
                    TtlInSeconds = 3600,
                    DnsARecords =
                    {
                        new DnsARecordInfo()
                        {
                            IPv4Address = IPAddress.Parse(publicIP1.Data.IPAddress)
                        }
                    }
                };
                var aRecord = await dnsZone.GetDnsARecords().CreateOrUpdateAsync(WaitUntil.Completed, "@", aInput);
                Utilities.Log("Updated root DNS zone " + dnsZone.Data.Name);

                // Prints the CName and A Records in the root DNS zone

                Utilities.Log("Getting CName record set in the root DNS zone " + dnsZoneName + "...");

                await foreach (var cnameRecordSet in dnsZone.GetDnsCnameRecords().GetAllAsync())
                {
                    Utilities.Log("Name: " + cnameRecordSet.Data.Name + " Canonical Name: " + cnameRecordSet.Data.Cname);
                }

                Utilities.Log("Getting ARecord record set in the root DNS zone " + dnsZoneName + "...");

                await foreach (var aRecordSet in dnsZone.GetDnsARecords().GetAllAsync())
                {
                    Utilities.Log("Name: " + aRecordSet.Data.Name);
                    foreach (var ipv4Address in aRecordSet.Data.DnsARecords)
                    {
                        Utilities.Log("  " + ipv4Address.IPv4Address);
                    }
                }

                //============================================================
                // Creates a child DNS zone

                Utilities.Log("Creating child DNS zone " + partnerSubDomainName + "...");

                DnsZoneData childZoneInput = new DnsZoneData("Global") { };
                var childZoneLro = await resourceGroup.GetDnsZones().CreateOrUpdateAsync(WaitUntil.Completed, partnerSubDomainName, childZoneInput);
                DnsZoneResource childZone = childZoneLro.Value;
                Utilities.Log("Created child DNS zone " + childZone.Data.Name);

                //============================================================
                // Adds NS records in the root dns zone to delegate partners.[customDomainName] to child dns zone

                Utilities.Log("Updating root DNS zone " + dnsZone.Data.Name + "...");
                DnsNSRecordData linkChildNSRecord = new DnsNSRecordData()
                {
                    TtlInSeconds = 3600,
                };
                foreach (var nameServer in childZone.Data.NameServers)
                {
                    linkChildNSRecord.DnsNSRecords.Add(new DnsNSRecordInfo() { DnsNSDomainName = nameServer });
                }
                _ = await dnsZone.GetDnsNSRecords().CreateOrUpdateAsync(WaitUntil.Completed, "partner", linkChildNSRecord);

                Utilities.Log("Root DNS zone updated");

                //============================================================
                // Creates a virtual machine with public IP

                // Create vnet
                VirtualNetworkResource vnet2 = await Utilities.CreateVirtualNetwork(resourceGroup, vnetName2);
                ResourceIdentifier subnetId2 = vnet2.Data.Subnets[0].Id;
                // Create public ip
                PublicIPAddressResource publicIP2 = await Utilities.CreatePublicIP(resourceGroup, publicIpName2);
                // Create network interface
                NetworkInterfaceResource nic2 = await Utilities.CreateNetworkInterface(resourceGroup, subnetId2, publicIP2.Id, nicName2);

                Utilities.Log("Creating a virtual machine with public IP...");

                VirtualMachineData vmInput2 = Utilities.GetDefaultVMInputData(resourceGroup, vmName2);
                vmInput2.NetworkProfile.NetworkInterfaces.Add(new VirtualMachineNetworkInterfaceReference() { Id = nic2.Id, Primary = true });
                var vmLro2 = await resourceGroup.GetVirtualMachines().CreateOrUpdateAsync(WaitUntil.Completed, vmName2, vmInput2);
                VirtualMachineResource vm2 = vmLro2.Value;
                Utilities.Log($"Created virtual machine: {vm2.Data.Name}");

                //Utilities.Log("Virtual machine created");

                //============================================================
                // Update child DNS zone by adding a A record pointing to virtual machine IPv4 address

                Utilities.Log("Updating child DNS zone " + partnerSubDomainName + "...");
                Utilities.Log("Updating root DNS zone " + dnsZoneName + "...");
                var childAInput = new DnsARecordData()
                {
                    TtlInSeconds = 3600,
                    DnsARecords =
                    {
                        new DnsARecordInfo()
                        {
                            IPv4Address = IPAddress.Parse(publicIP2.Data.IPAddress)
                        }
                    }
                };
                var childARecord = await childZone.GetDnsARecords().CreateOrUpdateAsync(WaitUntil.Completed, "@", childAInput);
                Utilities.Log("Updated root DNS zone " + childZone.Data.Name);

                Utilities.Log("Updated child DNS zone " + childZone.Data.Name);

                //============================================================
                // Removes A record entry from the root DNS zone

                Utilities.Log("Removing A Record from root DNS zone " + dnsZone.Data.Name + "...");
                await aRecord.Value.DeleteAsync(WaitUntil.Completed);

                Utilities.Log("Removed A Record from root DNS zone");

                //============================================================
                // Deletes the DNS zone

                Utilities.Log("Deleting child DNS zone " + childZone.Data.Name + "...");
                await childZone.DeleteAsync(WaitUntil.Completed);
                Utilities.Log("Deleted child DNS zone " + childZone.Data.Name);
            }
            finally
            {
                try
                {
                    if (_resourceGroupId is not null)
                    {
                        Utilities.Log($"Deleting Resource Group: {_resourceGroupId}");
                        await client.GetResourceGroupResource(_resourceGroupId).DeleteAsync(WaitUntil.Completed);
                        Utilities.Log($"Deleted Resource Group: {_resourceGroupId}");
                    }
                }
                catch (Exception)
                {
                    Utilities.Log("Did not create any resources in Azure. No clean up is necessary");
                }
            }
        }

        public static async Task Main(string[] args)
        {
            try
            {
                //=================================================================
                // Authenticate
                var clientId = Environment.GetEnvironmentVariable("CLIENT_ID");
                var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET");
                var tenantId = Environment.GetEnvironmentVariable("TENANT_ID");
                var subscription = Environment.GetEnvironmentVariable("SUBSCRIPTION_ID");
                ClientSecretCredential credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
                ArmClient client = new ArmClient(credential, subscription);

                await RunSample(client);
            }
            catch (Exception e)
            {
                Utilities.Log(e);
            }
        }
    }
}
