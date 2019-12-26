#
#	LEAN Algorithm Docker Container November-2016
#	Cross platform deployment for multiple brokerages
#

FROM quantconnect/lean:foundation

MAINTAINER QuantConnect <contact@quantconnect.com>

RUN apt-get update && apt-get install -y nuget nano vim wget tmux net-tools

WORKDIR /root/Lean
ADD . .
#################################
# Option 1: Download from Master
# RUN \
#	wget https://github.com/QuantConnect/Lean/archive/master.zip && \
#	unzip master.zip -d /root/Lean && \
#	cd /root/Lean
RUN nuget update -self
	#sed -i 's/4.5/4.0/' Algorithm.VisualBasic/QuantConnect.Algorithm.VisualBasic.vbproj && \
RUN nuget restore QuantConnect.Lean.sln -NonInteractive
RUN xbuild /t:clean /property:Configuration=Release
RUN xbuild /property:Configuration=Release

################################
# Option 2: Run Local Binaries:
#COPY ./Launcher/bin/Release /root/Lean/Launcher/bin/Release
#################################

# Finally.
WORKDIR /root/Lean/Launcher/bin/Release
CMD [ "mono", "QuantConnect.Lean.Launcher.exe"] # Run app

# Usage: 
# docker build -t quantconnect/lean:foundation -f DockerfileLeanFoundation .
# docker build -t quantconnect/lean:algorithm -f Dockerfile .
# docker run -v "(absolute to your data folder):/root/Lean/Data" quantconnect/lean:algorithm 
