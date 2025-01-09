﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace CCOF.Infrastructure.Plugins.AFS_History
{
    public class ApprovableFeeScheduleHistoryCreation : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            // Obtain the tracing service
            ITracingService tracingService =
            (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            // Obtain the execution context from the service provider.  
            IPluginExecutionContext context = (IPluginExecutionContext)
                serviceProvider.GetService(typeof(IPluginExecutionContext));

            if (context.InputParameters.Contains("Target") &&
                context.InputParameters["Target"] is Entity)
            {
                // Obtain the target entity from the input parameters.  
                Entity entity = (Entity)context.InputParameters["Target"];

                // Obtain the IOrganizationService instance which you will need for  
                // web service calls.  
                IOrganizationServiceFactory serviceFactory =
                    (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);
                tracingService.Trace("Starting AFS History plugin");
                try
                {
                    Entity mtfi = null;
                    string providerResponse = string.Empty;
                    Entity afsHistory = new Entity("ccof_approvable_fee_schedule_history");
                    switch (entity.LogicalName)
                    {
                        case "ccof_change_request_mtfi":
                            //Retrieve MTFI record
                            mtfi = service.Retrieve(entity.LogicalName, entity.Id, new ColumnSet("ccof_mtfi_qcdecision", "ownerid", "ccof_afs_process_began_on", "statuscode", "ccof_ccfri"));
                            var applicationCCFRI = service.Retrieve("ccof_applicationccfri", mtfi.GetAttributeValue<EntityReference>("ccof_ccfri").Id, new ColumnSet("ccof_afs_status_mtfi"));
                            providerResponse = applicationCCFRI.FormattedValues["ccof_afs_status_mtfi"].ToString();
                            break;
                        case "ccof_applicationccfri":
                            var query = new QueryExpression()
                            {
                                EntityName = "ccof_change_request_mtfi",
                                ColumnSet = new ColumnSet("ccof_mtfi_qcdecision", "ownerid", "ccof_afs_process_began_on", "statuscode"),
                                Criteria = new FilterExpression()
                                {
                                    Conditions =
                                    {
                                        new ConditionExpression("ccof_ccfri",ConditionOperator.Equal,entity.Id)
                                    }
                                }
                            };
                            mtfi = service.RetrieveMultiple(query).Entities.First();
                            applicationCCFRI = service.Retrieve(entity.LogicalName, entity.Id, new ColumnSet("ccof_afs_status_mtfi"));
                            providerResponse = entity.Attributes.Contains("ccof_afs_status_mtfi") ? applicationCCFRI.FormattedValues["ccof_afs_status_mtfi"].ToString() : null;
                            break;
                    }                    

                    if (mtfi != null && ((OptionSetValue)mtfi.Attributes["ccof_mtfi_qcdecision"]).Value == 100000007)
                    {
                        
                        string preChoiceLabel = string.Empty;
                        if (context.PreEntityImages.Contains("PreImage") && context.PreEntityImages["PreImage"] is Entity preImage)
                        {
                            var ownerId = preImage.Contains("ownerid") ? ((EntityReference)preImage["ownerid"]).Id : new Guid();
                            afsHistory["ccof_previous_owner"] = new EntityReference("systemuser", ownerId);
                            preChoiceLabel = preImage.FormattedValues["statuscode"].ToString();
                            afsHistory["ccof_previous_status"] = preChoiceLabel;
                        }
                        else
                        {
                            afsHistory["ccof_previous_owner"] = new EntityReference("systemuser", mtfi.GetAttributeValue<EntityReference>("ownerid").Id);
                            afsHistory["ccof_previous_status"] = mtfi.FormattedValues["statuscode"].ToString();
                        }
                        afsHistory["ccof_current_status"] = mtfi.FormattedValues["statuscode"].ToString();

                        // add a new row
                        afsHistory["ccof_current_owner"] = new EntityReference("systemuser", mtfi.GetAttributeValue<EntityReference>("ownerid").Id);
                        afsHistory["ccof_log_date"] = DateTime.UtcNow;
                        afsHistory["ccof_regarding_id"] = new EntityReference(mtfi.LogicalName, mtfi.Id);
                        afsHistory["ccof_afs_process_began_on"] = mtfi.GetAttributeValue<DateTime>("ccof_afs_process_began_on");
                        afsHistory["ccof_provider_response_history"] = providerResponse;
                        Guid recordId = service.Create(afsHistory);
                        tracingService.Trace($"Record created successfully with ID: {recordId}");
                        tracingService.Trace("End App Status History plugin");
                    }

                }

                catch (FaultException<OrganizationServiceFault> ex)
                {
                    throw new InvalidPluginExecutionException("An error occurred in AppStatusHistory Plugin.", ex);
                }

                catch (Exception ex)
                {
                    tracingService.Trace("AppStatusHistory Plugin: {0}", ex.ToString());
                    throw;
                }
            }
        }
    }
}
