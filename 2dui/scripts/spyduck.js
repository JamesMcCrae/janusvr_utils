// returns all <Text> nodes from the room code an an array: [ [js_id, text], ... ]// example to get first result's text:// var results = GetRoomText()// results[0][0] // js_id// results[0][1] // textfunction GetRoomText(){	var return_results = new Array();	var roomcode = window.janus.roomcode()	var expression = new RegExp('<Text(.*?>)(.*)</Text>','gm');	var results = roomcode.match(expression)	if (results)	{		for (result_index in results)		{			var result = results[result_index];			if (result.length >= 2)			{				expression = new RegExp('<Text(.*?>)(.*?)</Text>');				var fullstring = result.match(expression)[2];				expression = new RegExp('js_id="(.*?)"');				var js_id = result.match(expression);				if (js_id.length >= 1)				{					js_id = js_id[1];				}				else				{					js_id = '';				}				return_results.push( [js_id, fullstring] );			}		}	}	return return_results;}