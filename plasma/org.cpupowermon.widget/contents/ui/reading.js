.pragma library

function parse(stdout) {
    var lines = String(stdout || "").split(/\r?\n/);
    for (var i = lines.length - 1; i >= 0; i--) {
        var line = lines[i].trim();
        if (line.length === 0) {
            continue;
        }
        if (line.charAt(0) === "{") {
            try {
                var object = JSON.parse(line);
                if (object && typeof object.watts === "number" && isFinite(object.watts)) {
                    return {
                        watts: object.watts,
                        available: object.available !== false,
                        error: object.error || ""
                    };
                }
            } catch (error) {
            }
            continue;
        }
        if (/^-?\d+(\.\d+)?$/.test(line)) {
            var value = parseFloat(line);
            if (isFinite(value)) {
                return { watts: value, available: true, error: "" };
            }
        }
    }
    return null;
}
