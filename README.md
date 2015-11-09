# nlog.targets.lumberjack
A custom target for [NLog](http://nlog-project.org/) that allows sending logs over the [Lumberjack protocol](https://github.com/elastic/logstash-forwarder/blob/master/PROTOCOL.md) to for example [Logstash](http://logstash.net/).

## Install

    nuget install nlog.targets.lumberjack

## Configuration

Configure Logstash as a target in your NLog.config file,

```xml
<?xml version="1.0" encoding="utf-8" ?>
<nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd"
      xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <extensions>
    <add assembly="NLog.Targets.Lumberjack" />
  </extensions>
  <targets>
    <target name="logstash" type="Lumberjack" host="127.0.01" port="5001" layout="${message}"/>
  </targets>
  <rules>
    <logger name="*" minLevel="Trace" appendTo="logstash"/>
  </rules>
</nlog>
```

### Options

* `host`: The IP address or host name where logs should be sent. (*Required*)
* `port`: Port number on host to connect to (default: 5000)
* `fingerprint`: If set, this will be used to verify the certificate hash of the remote server.
* `encoding`: Text encoding to be used for properties.

This target supports the standard NLog 
[layout](https://github.com/NLog/NLog/wiki/Layouts) directive to modify
the log message body.

## Caveats

At this moment no attempt is made to implement the windowing and `ACK` parts of the Lumberjack protocol.

## NLog

See more about NLog at: http://nlog-project.org
