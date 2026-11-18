#!/bin/bash
projectDirectory=PxMCP
projectFile="$projectDirectory/PxMCP.csproj"

git fetch origin dev test --quiet || exit 1

mergeBase=$(git merge-base origin/dev HEAD) || {
	echo "Unable to find a merge base with origin/dev."
	exit 1
}

changedFiles=$(git diff --name-only "$mergeBase" HEAD -- "$projectDirectory") || exit 1
if [[ -z $changedFiles ]]; then
	echo "No $projectDirectory changes in the PR; version check is not required."
	exit 0
fi

readVersion() {
	sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p'
}

versionNumber=$(readVersion < "$projectFile")
versionInDev=$(git show "origin/dev:$projectFile" | readVersion)
versionInTest=$(git show "origin/test:$projectFile" | readVersion)

isValidVersion() {
	[[ $1 =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]
}

if ! isValidVersion "$versionNumber" || ! isValidVersion "$versionInDev" || ! isValidVersion "$versionInTest"; then
	echo "Missing or invalid major.minor.patch version."
	exit 1
fi

echo "Version: PR $versionNumber, dev $versionInDev, test $versionInTest"

versionLessThan() {
	local IFS=.
	local -a leftParts rightParts
	read -ra leftParts <<< "$1"
	read -ra rightParts <<< "$2"

	for i in 0 1 2
	do
		if ((10#${leftParts[i]} < 10#${rightParts[i]}))
		then
			return 0
		fi
		if ((10#${leftParts[i]} > 10#${rightParts[i]}))
		then
			return 1
		fi
	done
	return 1
}


changedProjectFile=$(git diff --name-only "$mergeBase" HEAD -- "$projectFile") || exit 1
if [[ -n $changedProjectFile ]]; then
	versionForMerge=$versionNumber
	if versionLessThan "$versionForMerge" "$versionInDev"; then
		echo "Project version must not be lower than the version in dev."
		exit 1
	fi
else
	versionForMerge=$versionInDev
fi

if ! versionLessThan "$versionInTest" "$versionForMerge"; then
	echo "Version for the merged app must be greater than the version in test."
	exit 1
fi