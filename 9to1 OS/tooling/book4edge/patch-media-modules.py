#!/usr/bin/env python3
"""Patch pinned source for a bounded speaker test and sequenced camera power."""
from pathlib import Path
import sys

source = Path(sys.argv[1])

def replace_once(text, before, after):
    if text.count(before) != 1:
        raise ValueError('Pinned source does not match: ' + before[:70])
    return text.replace(before, after, 1)

audio = source/'sound/soc/qcom/x1e80100.c'
text = audio.read_text()
start = text.index('\t\t/*\n\t\t * Set limit of -3 dB')
end = text.index('\n\t\tbreak;', start)
text = text[:start] + '''		/* NP960XMA diagnostic ceiling: -30 dB digital, -3 dB PA.
		 * Only the two address-2 woofers have a playback route. This
		 * is deliberately quiet, not a calibrated smart-amp solution.
		 * Fail card setup if a required limit cannot be applied.
		 */
		if (snd_soc_limit_volume(card, "WSA WSA_RX0 Digital Volume", 54) ||
		    snd_soc_limit_volume(card, "WSA WSA_RX1 Digital Volume", 54) ||
		    snd_soc_limit_volume(card, "WSA2 WSA_RX0 Digital Volume", 54) ||
		    snd_soc_limit_volume(card, "WSA2 WSA_RX1 Digital Volume", 54) ||
		    snd_soc_limit_volume(card, "SpkrLeft PA Volume", 6) ||
		    snd_soc_limit_volume(card, "SpkrRight PA Volume", 6))
			return -EINVAL;''' + text[end:]
text = replace_once(text, '#include <linux/module.h>', '#include <linux/module.h>\n#include <linux/of.h>')
text = replace_once(text, '\tcard = devm_kzalloc(dev, sizeof(*card), GFP_KERNEL);',
    '\tif (!of_machine_is_compatible("samsung,galaxy-book4-edge-16"))\n\t\treturn -ENODEV;\n\n\tcard = devm_kzalloc(dev, sizeof(*card), GFP_KERNEL);')
audio.write_text(text)

camera = source/'drivers/media/i2c/ov02c10.c'
text = camera.read_text()
text = replace_once(text, '#include <linux/module.h>', '#include <linux/module.h>\n#include <linux/of.h>')
text = replace_once(text, '\tstruct gpio_desc *reset;', '\tstruct gpio_desc *reset;\n\tstruct gpio_descs *board_power;\n\tstruct gpio_desc *board_enable;')
text = replace_once(text, '\tfor (i = 0; i < ARRAY_SIZE(ov02c10_supply_names); i++)', '''	if (device_property_read_bool(dev, "samsung,np960xma-power-sequence")) {
		if (!of_machine_is_compatible("samsung,galaxy-book4-edge-16"))
			return -ENODEV;
		ov02c10->board_power = devm_gpiod_get_array(dev, "power", GPIOD_OUT_LOW);
		if (IS_ERR(ov02c10->board_power))
			return PTR_ERR(ov02c10->board_power);
		if (ov02c10->board_power->ndescs != 2 || !ov02c10->reset)
			return -EINVAL;
		ov02c10->board_enable = devm_gpiod_get(dev, "enable", GPIOD_OUT_LOW);
		if (IS_ERR(ov02c10->board_enable))
			return PTR_ERR(ov02c10->board_enable);
		return 0;
	}

	for (i = 0; i < ARRAY_SIZE(ov02c10_supply_names); i++)''')
text = replace_once(text, '''	regulator_bulk_disable(ARRAY_SIZE(ov02c10_supply_names),
			       ov02c10->supplies);''', '''	if (ov02c10->board_power) {
		gpiod_set_value_cansleep(ov02c10->board_power->desc[1], 0);
		usleep_range(1000, 1200);
		gpiod_set_value_cansleep(ov02c10->board_power->desc[0], 0);
		usleep_range(1000, 1200);
		gpiod_set_value_cansleep(ov02c10->board_enable, 0);
	} else {
		regulator_bulk_disable(ARRAY_SIZE(ov02c10_supply_names),
				       ov02c10->supplies);
	}''')
before='''	ret = regulator_bulk_enable(ARRAY_SIZE(ov02c10_supply_names),
				    ov02c10->supplies);'''
text = replace_once(text,before, '''	if (ov02c10->board_power) {
		gpiod_set_value_cansleep(ov02c10->board_enable, 1);
		gpiod_set_value_cansleep(ov02c10->board_power->desc[0], 1);
		usleep_range(1000, 1200);
		gpiod_set_value_cansleep(ov02c10->board_power->desc[1], 1);
		usleep_range(1000, 1200);
		ret = 0;
	} else {
		ret = regulator_bulk_enable(ARRAY_SIZE(ov02c10_supply_names),
					    ov02c10->supplies);
	}''')
text=replace_once(text,'\t\tusleep_range(5000, 5100);','\t\tusleep_range(10000, 11000);')
camera.write_text(text)
