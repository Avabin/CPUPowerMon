import QtQuick
import org.kde.plasma.plasmoid
import org.kde.plasma.core as PlasmaCore
import org.kde.plasma.plasma5support as P5Support
import org.kde.kirigami as Kirigami
import "reading.js" as Reading

PlasmoidItem {
    id: root

    Plasmoid.backgroundHints: PlasmaCore.Types.DefaultBackground | PlasmaCore.Types.ConfigurableBackground
    Plasmoid.title: i18n("CPU Power Monitor")
    preferredRepresentation: Plasmoid.formFactor === PlasmaCore.Types.Planar ? fullRepresentation : null

    readonly property int historySize: 60
    readonly property int minimumIntervalMs: 1000
    readonly property string command: (Plasmoid.configuration.command || "").trim()
    readonly property int pollIntervalMs: Math.max(minimumIntervalMs, Plasmoid.configuration.intervalMs || 1000)

    property real watts: 0
    property bool available: false
    property bool everUpdated: false
    property string activeCommand: ""
    property string lastError: ""
    property var history: []

    toolTipMainText: i18n("CPU Power Monitor")
    toolTipSubText: available
        ? i18n("%1 W package power", watts.toFixed(1))
        : (everUpdated ? i18n("Unavailable: %1", lastError) : i18n("Waiting for cpumonctl"))

    compactRepresentation: CompactRepresentation {
        available: root.available
        watts: root.watts
        history: root.history
    }

    fullRepresentation: FullRepresentation {
        available: root.available
        watts: root.watts
        history: root.history
        errorText: root.lastError
        everUpdated: root.everUpdated
        intervalMs: root.pollIntervalMs
        command: root.command
    }

    P5Support.DataSource {
        id: executable
        engine: "executable"
        interval: root.pollIntervalMs
        onNewData: function(sourceName, data) {
            root.handleOutput(data);
        }
        onSourceDisconnected: function(source) {
            if (source === root.activeCommand) {
                root.setUnavailable(i18n("cpumonctl stopped producing data"));
            }
        }
    }

    Timer {
        id: staleTimer
        interval: Math.max(4000, root.pollIntervalMs * 3)
        repeat: false
        onTriggered: {
            if (root.command.length === 0) {
                root.setUnavailable(i18n("No command configured"));
            } else if (!root.everUpdated) {
                root.setUnavailable(i18n("No data from %1", root.command));
            } else if (root.available) {
                root.setUnavailable(i18n("No data from cpumonctl for %1 ms", interval));
            }
        }
    }

    Component.onCompleted: applyCommand()

    onCommandChanged: applyCommand()

    function applyCommand() {
        if (activeCommand.length > 0) {
            executable.disconnectSource(activeCommand);
        }
        staleTimer.stop();
        activeCommand = root.command;
        if (activeCommand.length === 0) {
            setUnavailable(i18n("No command configured"));
            return;
        }
        everUpdated = false;
        available = false;
        lastError = "";
        history = [];
        executable.connectSource(activeCommand);
        staleTimer.start();
    }

    function handleOutput(data) {
        var stdout = data["stdout"] || "";
        var stderr = String(data["stderr"] || "").trim();
        var exitCode = Number(data["exit code"]);
        var exitStatus = Number(data["exit status"]);
        var failed = (isFinite(exitCode) && exitCode !== 0) || (isFinite(exitStatus) && exitStatus !== 0);
        var reading = Reading.parse(stdout);

        everUpdated = true;
        staleTimer.restart();

        if (failed || !reading || !reading.available) {
            var reason = stderr.length > 0 ? stderr : (reading && reading.error ? reading.error : i18n("No valid power value"));
            setUnavailable(reason);
            return;
        }

        watts = reading.watts;
        available = true;
        lastError = "";
        var next = history.concat([watts]);
        if (next.length > historySize) {
            next = next.slice(next.length - historySize);
        }
        history = next;
    }

    function setUnavailable(message) {
        available = false;
        lastError = message;
    }
}
