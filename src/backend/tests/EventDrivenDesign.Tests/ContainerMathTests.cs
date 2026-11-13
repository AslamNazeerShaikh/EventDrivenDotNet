using AwesomeAssertions;
using EventDrivenDesign.Api;
using Xunit;

namespace EventDrivenDesign.Tests;

public class ContainerMathTests
{
    [Fact]
    public void Cpu_percent_is_null_on_the_first_sample()
    {
        ContainerMath.CpuPercent(null, new CpuSample(100, 1000, 4)).Should().BeNull();
    }

    [Fact]
    public void Cpu_percent_is_the_share_of_system_time_times_the_cpus()
    {
        ContainerMath
            .CpuPercent(new CpuSample(100, 1_000, 4), new CpuSample(150, 2_000, 4))
            .Should()
            .BeApproximately(20.0, 1e-9); // 50 / 1000 × 4 × 100
    }

    [Fact]
    public void Cpu_percent_is_null_when_system_time_did_not_move()
    {
        ContainerMath
            .CpuPercent(new CpuSample(100, 1_000, 4), new CpuSample(150, 1_000, 4))
            .Should()
            .BeNull();
    }

    [Fact]
    public void Cpu_percent_is_null_after_a_counter_reset()
    {
        ContainerMath
            .CpuPercent(new CpuSample(500, 1_000, 4), new CpuSample(10, 2_000, 4))
            .Should()
            .BeNull();
    }

    [Fact]
    public void Memory_used_leaves_out_the_inactive_file_cache()
    {
        ContainerMath.MemoryUsed(1_000, 300).Should().Be(700);
        ContainerMath.MemoryUsed(1_000, null).Should().Be(1_000);
        ContainerMath.MemoryUsed(null, 300).Should().BeNull();
    }

    [Theory]
    [InlineData("gateway-prs-a", "Edge gateways", 1)]
    [InlineData("plant-kafka", "Plant Kafka", 2)]
    [InlineData("plant-topics", "Plant Kafka", 2)]
    [InlineData("mm2-1", "MirrorMaker 2", 3)]
    [InlineData("central-kafka", "Central Kafka", 4)]
    [InlineData("consumer-2", "Store writers", 5)]
    [InlineData("central-db", "PostgreSQL", 6)]
    [InlineData("api-1", "Control room", 7)]
    [InlineData("dashboard", "Control room", 7)]
    [InlineData("something-else", "Other", 8)]
    public void Tier_follows_the_data_flow(string service, string tier, int order)
    {
        ContainerMath.TierOf(service).Should().Be((tier, order));
    }

    [Fact]
    public void Stats_are_parsed_from_the_engine_json()
    {
        const string json = """
            {
              "cpu_stats": { "cpu_usage": { "total_usage": 150 }, "system_cpu_usage": 2000, "online_cpus": 4 },
              "memory_stats": { "usage": 1000, "limit": 4000, "stats": { "inactive_file": 300 } },
              "networks": { "eth0": { "rx_bytes": 10, "tx_bytes": 20 }, "eth1": { "rx_bytes": 1, "tx_bytes": 2 } },
              "blkio_stats": { "io_service_bytes_recursive": [
                { "major": 8, "minor": 0, "op": "read", "value": 5 },
                { "major": 8, "minor": 0, "op": "write", "value": 7 },
                { "major": 8, "minor": 16, "op": "Read", "value": 1 }
              ] }
            }
            """;

        ContainerMath
            .ParseStats(json)
            .Should()
            .Be(new ContainerStats(new CpuSample(150, 2000, 4), 700, 4000, 11, 22, 6, 7));
    }

    [Fact]
    public void Stats_tolerate_missing_networks_and_null_block_io()
    {
        const string json = """
            {
              "cpu_stats": { "cpu_usage": { "total_usage": 0 } },
              "memory_stats": {},
              "blkio_stats": { "io_service_bytes_recursive": null }
            }
            """;

        ContainerMath
            .ParseStats(json)
            .Should()
            .Be(new ContainerStats(null, null, null, null, null, null, null));
    }

    [Fact]
    public void Disk_usage_is_parsed_from_system_df()
    {
        const string json = """
            {
              "Images": [ { "Id": "sha256:aa", "Size": 1000 } ],
              "Containers": [ { "Id": "c1", "SizeRw": 50 }, { "Id": "c2" } ],
              "Volumes": [ { "Name": "v1", "UsageData": { "Size": 300, "RefCount": 1 } }, { "Name": "v2", "UsageData": null } ]
            }
            """;

        var df = ContainerMath.ParseDiskUsage(json);

        df.ImageBytes.Should()
            .BeEquivalentTo(new Dictionary<string, long> { ["sha256:aa"] = 1000 });
        df.WritableBytes.Should().BeEquivalentTo(new Dictionary<string, long> { ["c1"] = 50 });
        df.VolumeBytes.Should().BeEquivalentTo(new Dictionary<string, long> { ["v1"] = 300 });
    }

    [Fact]
    public void Containers_are_listed_with_tier_ports_volumes_and_health()
    {
        const string json = """
            [
              {
                "Id": "c1", "Names": ["/edd-gateway-prs-a-1"], "Image": "edd-gateway", "ImageID": "sha256:aa",
                "State": "running", "Status": "Up 5 minutes (healthy)",
                "Labels": { "com.docker.compose.service": "gateway-prs-a" },
                "Ports": [
                  { "IP": "0.0.0.0", "PrivatePort": 8080, "PublicPort": 18080, "Type": "tcp" },
                  { "IP": "::", "PrivatePort": 8080, "PublicPort": 18080, "Type": "tcp" },
                  { "PrivatePort": 9000, "Type": "tcp" }
                ],
                "Mounts": [
                  { "Type": "volume", "Name": "edd_gw-data", "Destination": "/data" },
                  { "Type": "bind", "Source": "/var/run/docker.sock", "Destination": "/var/run/docker.sock" }
                ]
              }
            ]
            """;

        var listed = ContainerMath.ParseList(json);

        listed
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new ListedContainer(
                    "c1",
                    "edd-gateway-prs-a-1",
                    "gateway-prs-a",
                    "running",
                    "healthy",
                    "Up 5 minutes (healthy)",
                    "edd-gateway",
                    "sha256:aa",
                    [new(8080, 18080, "tcp"), new(9000, null, "tcp")],
                    [new("edd_gw-data", "/data")]
                )
            );
    }

    [Theory]
    [InlineData("Up 5 minutes (healthy)", "healthy")]
    [InlineData("Up 5 seconds (health: starting)", "starting")]
    [InlineData("Up 1 hour (unhealthy)", "unhealthy")]
    [InlineData("Exited (0) 2 minutes ago", null)]
    public void Health_is_read_from_the_status_text(string status, string? health)
    {
        ContainerMath.HealthOf(status).Should().Be(health);
    }
}
