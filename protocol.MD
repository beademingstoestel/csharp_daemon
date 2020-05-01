# BREATHNEY COMMUNICATION PROTOTCOL OVERVIEW

## IMPORTANT NOTES

- The byte order used by all parties is assumed to be big endian.
- In future versions a redesign of the protocol to be completely binary instead of a mix between ASCII and binary will lead to cleaner code.

## SETTINGS

Settings are sent when communication between uC and PC are (re-)started.
Communicated in plain text, with message counter checksum.
The checksum is 1 byte, computed as the XOR of all previous bytes in the message.

Format: 
	Setting=value(float)=messageID(1 byte)=checksum(1 byte) 
	
Example: 
	ACTIVE=1.0=F=?

List of settings:

	ALARM		// 0  alarm state
	RR          // 1  respiratory rate
	VT          // 2  tidal volume
	PK          // 3  peak pressure
	PS          // 4  support pressure
	PP          // 5  peep
	IE          // 6  I/E as float ==> 1:2 = 0.33
	RP          // 7  Ramp time
	TS          // 8  Flow trigger
	TP          // 9  Pressure trigger
	ADPK        // 10 Peak pressure deviation
	ADVT        // 11 Tidal volume deviation
	ADPP        // 12 Peep pressure deviation
	MODE        // 13 Mode: BIT0: 0 = pressure trigger, 1 = flow trigger; BIT1: enable triggers; BIT2: 0 = enable volume limit
	ACTIVE      // 14 Active: 0 = disabled, 1 = startup peep, 2 = active
	MT          // 15 Mute: 0 = no mute / sound, 1 = mute, no sound
	FIO2        // 16 Oxygen level
	ADFIO2		// 17 Oxygen level deviation
	LPK			// 18 Lower limit PK
	HPK			// 19 Upper limit PK
	HRR			// 20 Upper limit RR
	FW          // 21 Firmware version
	
These messages require an acknowledgement.
	
## MEASURED VALUES

Measured values are sent approx every 50 ms
Communicated as a binary string (to reduce message size).
The start is indicated by start of text (0x02), followed by message type (0x01) and the length of the message, after which all the measurement values follow.
The checksum is calculated on every byte preceding it.
The end is indicated by the newline character (0x0A)

Format:
  0x02 
  0x01 
  {one byte message length}           
  {one byte trigger value}            value between 0 or 1
  {two bytes volume value}            value in ml of the current volume, scaled by a factor 10
  {two bytes pressure value}          value in cmH2O of the current pressure, scaled by a factor 100
  {two bytes target pressure value}   value in cmH2O of the target pressure, scaled by a factor 100
  {two bytes respiratory rate}        respiratory rate in breaths per minute, scaled by a factor 100
  {two bytes flow value}              current flow in L/min, scaled by a factor 100
  {two bytes FIO2 value}              percentage FIO2 between 21 and 100
  {4 bytes timestamp arduino}         timestamp of the number of milliseconds since startup
  {CRC byte}
  0x0A
	
These messages don't require an acknowledgement. Messages with the wrong checksum are discarded by the recipient.

## ALARMS & CPU

Alarms are being sent every 1 second. 
Communicated in plain text as un unsigned integer, with message counter checksum.
The checksum is 1 byte, computed as the XOR of all previous bytes in the message.

Format:
	ALARM=value=counter=checksum
	
Alarm bits:
	0:  'fio2 out of bounds'
    1:  'Pressure not within thresholds'
    2:  'Volume not within thresholds'
    3:  'Oxygen calibration failed'
    4:  'Pressure sensor error'
    5:  'Machine is overheating'
    6:  'Flow sensor error'
    7:  'Pressure sensor calibration failed'
    8:  'Flow sensor calibration failed'
    9:  'Limit switch sensor error'
    10: 'HALL sensor error'
    11: 'No external power, switch to battery'
    12: 'Battery low'
    13: 'Battery critical'
    14: 'Fan not operational'
    15: 'GUI not found'
    
The PC must reply with its own ALARM message to indicate it's still connected. The PC will either send value 0 or 1 indicating whether or not the alarm should be active.

## CPU USAGE

Similar to the alarms, but on a 3 second interval.
Number between 0 and 100 to indicate % use.

## ACK

Acknowledgement messages are used to confirm correct receipt of the setting values.
Communicated in plain text, with message id of the message to be ack'ed and checksum.
The checksum is 1 byte, computed as the XOR of all previous bytes in the message.

Format:
	ACK=messageID=checksum
