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
        private readonly string _region;
        private readonly string _accessKeyId;
        private readonly string _secret;
        private readonly string _domainName;
        private readonly string _rr;
        private readonly string _ip;
        private readonly DefaultAcsClient _acsClient;

        public Worker(ILogger<Worker> logger,
                      IHttpClientFactory httpClientFactory,
                      IConfiguration configuration)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;

            _region = configuration["ali:region"] ?? "cn-hangzhou";
            _accessKeyId = configuration["ali:accessKeyId"];
            _secret = configuration["ali:secret"];
            _domainName = configuration["domainName"];
            _rr = configuration["rr"];
            _ip = configuration["ip"] ?? "https://api.lcrun.com/ip";

            var profile = DefaultProfile.GetProfile(_region, _accessKeyId, _secret);
            _acsClient = new DefaultAcsClient(profile);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    string ip = await GetPublicNetWorkIP();

                    (string recordId, string currentIP) = GetDNSRecord();

                    if (string.IsNullOrWhiteSpace(recordId))
                    {
                        AddDNS(ip);
                    }
                    else if (!ip.Equals(currentIP))
                    {
                        UpdateDNS(recordId, ip);
                    }
                    else
                    {
                        _logger.LogInformation($"[IP no change] {_rr}.{_domainName} -> {ip}");
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

            _logger.LogInformation($"Stop Work: stoppingToken:{stoppingToken.IsCancellationRequested}");
        }

        private (string recordId, string currentIP) GetDNSRecord()
        {
            var recordsRequest = new DescribeDomainRecordsRequest
            {
                DomainName = _domainName,
                RRKeyWord = _rr
            };
            var recordResponse = _acsClient.GetAcsResponse(recordsRequest);
            var recordStringResult = Encoding.Default.GetString(recordResponse.HttpResponse.Content);
            var recordJsonResult = JsonConvert.DeserializeObject<JObject>(recordStringResult);
            if (recordJsonResult["TotalCount"].Value<int>() == 0)
            {
                return (null, null);
            }
            else
            {
                var recordId = recordJsonResult["DomainRecords"]["Record"][0]["RecordId"].ToString();
                var currentIP = recordJsonResult["DomainRecords"]["Record"][0]["Value"].ToString();
                return (recordId, currentIP);
            }
        }

        private void UpdateDNS(string recordId, string ip)
        {
            var updateRequest = new UpdateDomainRecordRequest()
            {
                RecordId = recordId,
                RR = ip,
                Type = "A",
                _Value = ip
            };
            _ = _acsClient.GetAcsResponse(updateRequest);

            _logger.LogInformation($"[Update DNS] {_rr}.{_domainName} ->  {ip}");
        }

        private void AddDNS(string ip)
        {
            var addRequest = new AddDomainRecordRequest()
            {
                DomainName = _domainName,
                RR = _rr,
                Type = "A",
                _Value = ip
            };
            var addResponse = _acsClient.GetAcsResponse(addRequest);
            var addStringResult = Encoding.Default.GetString(addResponse.HttpResponse.Content);
            var addJsonResult = JsonConvert.DeserializeObject<JObject>(addStringResult);

            _logger.LogInformation(addJsonResult.ToString());
            _logger.LogInformation($"[Add DNS] {_rr}.{_domainName} -> {ip}");
        }

        private async Task<string> GetPublicNetWorkIP()
        {
            var httpClient = _httpClientFactory.CreateClient();
            var ip = await httpClient.GetStringAsync(_ip);
            _logger.LogInformation($"[Public NetWork IP] {ip}");
            return ip;
        }
    }
}
