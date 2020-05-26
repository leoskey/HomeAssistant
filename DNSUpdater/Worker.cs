using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aliyun.Acs.Alidns.Model.V20150109;
using Aliyun.Acs.Core;
using Aliyun.Acs.Core.Exceptions;
using Aliyun.Acs.Core.Profile;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DNSUpdater
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly string _region;
        private readonly string _accessKeyId;
        private readonly string _secret;
        private readonly string _domainName;
        private readonly string _rr;

        public Worker(ILogger<Worker> logger,
                      IHttpClientFactory httpClientFactory,
                      IConfiguration configuration)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;

            _region = configuration["ali:region"] ?? "cn-hangzhou";
            _accessKeyId = configuration["ali:accessKeyId"];
            _secret = configuration["ali:secret"];
            _domainName = configuration["domainName"];
            _rr = configuration["rr"];
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            IClientProfile profile = DefaultProfile.GetProfile(_region, _accessKeyId, _secret);
            DefaultAcsClient client = new DefaultAcsClient(profile);

            var httpClient = _httpClientFactory.CreateClient();

            var recordsRequest = new DescribeDomainRecordsRequest
            {
                DomainName = _domainName,
                RRKeyWord = _rr
            };

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var ip = await httpClient.GetStringAsync("http://ifconfig.me");

                    var recordResponse = client.GetAcsResponse(recordsRequest);
                    var recordStringResult = Encoding.Default.GetString(recordResponse.HttpResponse.Content);
                    var recordJsonResult = JsonConvert.DeserializeObject<JObject>(recordStringResult);
                    if (recordJsonResult["TotalCount"].Value<int>() == 0)
                    {
                        // 新增
                        var addRequest = new AddDomainRecordRequest()
                        {
                            DomainName = _domainName,
                            RR = _rr,
                            Type = "A",
                            _Value = ip
                        };
                        var addResponse = client.GetAcsResponse(addRequest);
                        var addStringResult = Encoding.Default.GetString(recordResponse.HttpResponse.Content);
                        var addJsonResult = JsonConvert.DeserializeObject<JObject>(addStringResult);

                        _logger.LogInformation($"{DateTimeOffset.Now} 新增：{_rr}.{_domainName}->{ip}");
                    }
                    else
                    {
                        // 更新
                        var recordId = recordJsonResult["DomainRecords"]["Record"][0]["RecordId"].ToString();
                        var currentIP = recordJsonResult["DomainRecords"]["Record"][0]["Value"].ToString();
                        if (ip.Equals(currentIP))
                        {
                            _logger.LogInformation($"{DateTimeOffset.Now} IP未变更：{_rr}.{_domainName}->{ip}");
                        }
                        else
                        {
                            var updateRequest = new UpdateDomainRecordRequest()
                            {
                                RecordId = recordId,
                                RR = ip,
                                Type = "A",
                                _Value = ip
                            };
                            var updateResponse = client.GetAcsResponse(updateRequest);
                            var updateStringResult = Encoding.Default.GetString(recordResponse.HttpResponse.Content);
                            var updateJsonResult = JsonConvert.DeserializeObject<JObject>(updateStringResult);

                            _logger.LogInformation($"{DateTimeOffset.Now} 更新：{_rr}.{_domainName}->{ip}");
                        }
                    }

                }
                catch (ServerException e)
                {
                    _logger.LogError(e, "ServerException");
                }
                catch (ClientException e)
                {
                    _logger.LogError(e, "ClientException");
                }

                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}
